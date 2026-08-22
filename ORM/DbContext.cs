using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.ORM.Entity;
using MigrationBase = mersolutionCore.ORM.Migration.Migration;
using MigrationSchema = mersolutionCore.ORM.Migration.Schema;
using MigrationStatus = mersolutionCore.ORM.Migration.MigrationStatus;
using MigrationMigrator = mersolutionCore.ORM.Migration.Migrator;
using ColumnBuilder = mersolutionCore.ORM.Migration.ColumnBuilder;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Database context - EF Core style
    /// </summary>
    public abstract class DbContext
    {
        private readonly Func<DbCommandBase> _connectionFactory;
        private readonly List<Type> _modelTypes = new List<Type>();

        protected DbContext(Func<DbCommandBase> connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            
            // Configure ORM
            ModelBase.Configure(_connectionFactory);
            
            // Discover DbSet properties
            DiscoverModels();
        }

        /// <summary>
        /// Ensure database is created and migrations are applied
        /// </summary>
        public void EnsureCreated()
        {
            var migrator = new MigrationMigrator(_connectionFactory);

            foreach (var modelType in _modelTypes)
            {
                var migration = CreateMigrationForModel(modelType);
                if (migration != null)
                {
                    migrator.Add(migration);
                }
            }

            migrator.Migrate();
        }

        /// <summary>
        /// Drop all tables
        /// </summary>
        public void EnsureDeleted()
        {
            var migrator = new MigrationMigrator(_connectionFactory);

            foreach (var modelType in _modelTypes.AsEnumerable().Reverse())
            {
                var migration = CreateMigrationForModel(modelType);
                if (migration != null)
                {
                    migrator.Add(migration);
                }
            }

            migrator.Reset();
        }

        /// <summary>
        /// Drop all tables and recreate (fresh start)
        /// </summary>
        public void EnsureFresh()
        {
            EnsureDeleted();
            EnsureCreated();
        }

        /// <summary>
        /// Get migration status
        /// </summary>
        public MigrationStatus GetMigrationStatus()
        {
            var migrator = new MigrationMigrator(_connectionFactory);

            foreach (var modelType in _modelTypes)
            {
                var migration = CreateMigrationForModel(modelType);
                if (migration != null)
                {
                    migrator.Add(migration);
                }
            }

            return migrator.Status();
        }

        /// <summary>
        /// Discover all DbSet<T> properties
        /// </summary>
        private void DiscoverModels()
        {
            var properties = GetType().GetProperties()
                .Where(p => p.PropertyType.IsGenericType && 
                            p.PropertyType.GetGenericTypeDefinition() == typeof(MerSet<>));

            foreach (var prop in properties)
            {
                var modelType = prop.PropertyType.GetGenericArguments()[0];
                _modelTypes.Add(modelType);

                // Create DbSet instance
                var dbSetType = typeof(MerSet<>).MakeGenericType(modelType);
                var dbSet = Activator.CreateInstance(dbSetType);
                prop.SetValue(this, dbSet);
            }
        }

        /// <summary>
        /// Create auto migration from model attributes
        /// </summary>
        private MigrationBase CreateMigrationForModel(Type modelType)
        {
            return new AutoMigration(modelType, _connectionFactory);
        }
    }

    /// <summary>
    /// MerSet - represents a table (mersolution entity set)
    /// </summary>
    public class MerSet<T> where T : Model<T>, new()
    {
        public List<T> ToList() => Model<T>.All();
        public T Find(object id) => Model<T>.Find(id);
        public T FirstOrDefault() => Model<T>.Query().First();
        public QueryBuilder<T> Where(string column, object value) => Model<T>.Where(column, value);
        public QueryBuilder<T> Where(string column, string op, object value) => Model<T>.Where(column, op, value);
        public int Count() => Model<T>.Query().Count();
        
        public T Add(T entity)
        {
            entity.Save();
            return entity;
        }

        public void Remove(T entity)
        {
            entity.Delete();
        }
    }

    /// <summary>
    /// Auto migration - creates table from model attributes
    /// </summary>
    internal class AutoMigration : MigrationBase
    {
        private readonly Type _modelType;
        private readonly ModelMetadata _metadata;
        private readonly Func<DbCommandBase> _connectionFactory;

        public override string Version { get; }
        public override string Description { get; }

        public AutoMigration(Type modelType, Func<DbCommandBase> connectionFactory)
        {
            _modelType = modelType;
            _connectionFactory = connectionFactory;
            _metadata = GetMetadata(modelType);
            
            Version = $"Auto_{_metadata.TableName}";
            Description = $"Create {_metadata.TableName} table";
        }

        private ModelMetadata GetMetadata(Type type)
        {
            var method = typeof(ModelBase).GetMethod("GetMetadata", BindingFlags.NonPublic | BindingFlags.Static);
            var generic = method.MakeGenericMethod(type);
            return (ModelMetadata)generic.Invoke(null, null);
        }

        public override void Up(MigrationSchema schema)
        {
            schema.CreateTable(_metadata.TableName, table =>
            {
                foreach (var prop in _metadata.Properties)
                {
                    if (prop.IsPrimaryKey)
                    {
                        if (prop.IsAutoIncrement)
                            table.Id(prop.ColumnName);
                        else
                            table.Integer(prop.ColumnName).NotNull();
                        continue;
                    }

                    var colType = prop.PropertyInfo.PropertyType;
                    colType = Nullable.GetUnderlyingType(colType) ?? colType;

                    ColumnBuilder col;

                    if (colType == typeof(string))
                    {
                        col = table.String(prop.ColumnName, prop.Length > 0 ? prop.Length : 255);
                    }
                    else if (colType == typeof(int) || colType == typeof(long))
                    {
                        col = table.Integer(prop.ColumnName);
                    }
                    else if (colType == typeof(decimal) || colType == typeof(double) || colType == typeof(float))
                    {
                        col = table.Decimal(prop.ColumnName, 18, 2);
                    }
                    else if (colType == typeof(bool))
                    {
                        col = table.Boolean(prop.ColumnName);
                    }
                    else if (colType == typeof(DateTime))
                    {
                        col = table.DateTime(prop.ColumnName);
                    }
                    else if (colType == typeof(Guid))
                    {
                        col = table.String(prop.ColumnName, 36);
                    }
                    else
                    {
                        col = table.String(prop.ColumnName, 255);
                    }

                    if (prop.Nullable || Nullable.GetUnderlyingType(prop.PropertyInfo.PropertyType) != null)
                        col.Nullable();
                    else
                        col.NotNull();
                }
            });
        }

        public override void Down(MigrationSchema schema)
        {
            schema.DropTableIfExists(_metadata.TableName);
        }
    }
}
