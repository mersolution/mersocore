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
        /// Discover all MerSet&lt;T&gt; properties
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
        public T? Find(object id) => Model<T>.Find(id);
        public T? FirstOrDefault() => Model<T>.Query().First();
        public QueryBuilder<T> Where(string column, object? value) => Model<T>.Where(column, value);
        public QueryBuilder<T> Where(string column, string op, object? value) => Model<T>.Where(column, op, value);
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

        public System.Threading.Tasks.Task<List<T>> ToListAsync(System.Threading.CancellationToken cancellationToken = default)
            => Model<T>.AllAsync(cancellationToken);

        public System.Threading.Tasks.Task<T?> FindAsync(object id, System.Threading.CancellationToken cancellationToken = default)
            => Model<T>.FindAsync(id, cancellationToken);

        public System.Threading.Tasks.Task<T?> FirstOrDefaultAsync(System.Threading.CancellationToken cancellationToken = default)
            => Model<T>.Query().FirstAsync(cancellationToken);

        public System.Threading.Tasks.Task<int> CountAsync(System.Threading.CancellationToken cancellationToken = default)
            => Model<T>.Query().CountAsync(cancellationToken: cancellationToken);

        public System.Threading.Tasks.Task<T> AddAsync(T entity, System.Threading.CancellationToken cancellationToken = default)
            => Model<T>.CreateAsync(entity, cancellationToken);

        public System.Threading.Tasks.Task<bool> RemoveAsync(T entity, System.Threading.CancellationToken cancellationToken = default)
            => entity.DeleteAsync(cancellationToken);
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
            _metadata = ModelMetadata.GetMetadata(modelType);

            Version = $"Auto_{_metadata.TableName}";
            Description = $"Create {_metadata.TableName} table";
        }

        public override void Up(MigrationSchema schema)
        {
            schema.CreateTable(_metadata.TableName, table =>
            {
                foreach (var prop in _metadata.Properties)
                {
                    var colType = prop.PropertyInfo.PropertyType;
                    colType = Nullable.GetUnderlyingType(colType) ?? colType;

                    if (prop.IsPrimaryKey)
                    {
                        if (prop.IsAutoIncrement)
                        {
                            if (colType == typeof(long) || colType == typeof(ulong))
                                table.BigId(prop.ColumnName);
                            else
                                table.Id(prop.ColumnName);
                        }
                        else if (colType == typeof(Guid))
                        {
                            table.Uuid(prop.ColumnName);
                        }
                        else
                        {
                            ColumnFor(table, prop, colType).NotNull();
                            table.Primary(prop.ColumnName);
                        }
                        continue;
                    }

                    var col = ColumnFor(table, prop, colType);

                    if (prop.Nullable || Nullable.GetUnderlyingType(prop.PropertyInfo.PropertyType) != null)
                        col.Nullable();
                    else
                        col.NotNull();
                }
            });
        }

        private static ColumnBuilder ColumnFor(mersolutionCore.ORM.Migration.TableBuilder table, PropertyMetadata prop, Type colType)
        {
            // Converted properties: enum names fit a short string, JSON / encrypted text needs a text column
            if (prop.Converter is EnumStringConverter)
                return table.String(prop.ColumnName, prop.Length > 0 ? prop.Length : 50);
            if (prop.Converter != null)
                return prop.Length > 0 ? table.String(prop.ColumnName, prop.Length) : table.Text(prop.ColumnName);

            if (colType == typeof(string))
                return table.String(prop.ColumnName, prop.Length > 0 ? prop.Length : 255);
            if (colType == typeof(long) || colType == typeof(ulong) || colType == typeof(uint))
                return table.BigInteger(prop.ColumnName);
            if (colType == typeof(int) || colType == typeof(short) || colType == typeof(ushort) ||
                colType == typeof(byte) || colType == typeof(sbyte) || colType.IsEnum)
                return table.Integer(prop.ColumnName);
            if (colType == typeof(decimal))
                return table.Decimal(prop.ColumnName, 18, 2);
            if (colType == typeof(double))
                return table.Double(prop.ColumnName);
            if (colType == typeof(float))
                return table.Float(prop.ColumnName);
            if (colType == typeof(bool))
                return table.Boolean(prop.ColumnName);
            if (colType == typeof(DateTime))
                return table.DateTime(prop.ColumnName);
            if (colType == typeof(TimeSpan) || DateTypes.IsTimeOnly(colType))
                return table.Time(prop.ColumnName);
            if (DateTypes.IsDateOnly(colType))
                return table.Date(prop.ColumnName);
            if (colType == typeof(byte[]))
                return table.Binary(prop.ColumnName, prop.Length);
            if (colType == typeof(Guid))
                return table.String(prop.ColumnName, 36);

            return table.String(prop.ColumnName, prop.Length > 0 ? prop.Length : 255);
        }

        public override void Down(MigrationSchema schema)
        {
            schema.DropTableIfExists(_metadata.TableName);
        }
    }
}
