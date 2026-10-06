using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// System.Text.Json support for models: [Hidden] properties (and MakeHidden / MakeVisible per instance)
    /// are left out when a model is serialized directly, e.g. returned from an ASP.NET Core endpoint.
    /// <code>
    /// builder.Services.ConfigureHttpJsonOptions(o =&gt; MersoJson.Configure(o.SerializerOptions)); // minimal APIs
    /// builder.Services.AddControllers().AddJsonOptions(o =&gt; MersoJson.Configure(o.JsonSerializerOptions));
    /// var json = JsonSerializer.Serialize(user, MersoJson.Options);
    /// </code>
    /// </summary>
    public static class MersoJson
    {
        private static readonly Lazy<JsonSerializerOptions> _options =
            new Lazy<JsonSerializerOptions>(() => Configure(new JsonSerializerOptions()));

        /// <summary>
        /// Ready-made options (default System.Text.Json settings + hidden model properties)
        /// </summary>
        public static JsonSerializerOptions Options => _options.Value;

        /// <summary>
        /// Add the model rules to existing options (keeps their resolver, naming policy and converters).
        /// Call it before the options are first used.
        /// </summary>
        public static JsonSerializerOptions Configure(JsonSerializerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var resolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
            options.TypeInfoResolver = resolver.WithAddedModifier(HideHiddenProperties);
            return options;
        }

        /// <summary>
        /// Type info modifier: skip [Hidden] model properties (usable with your own resolver chain)
        /// </summary>
        public static void HideHiddenProperties(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(ModelBase).IsAssignableFrom(typeInfo.Type))
                return;

            var metadata = ModelMetadata.GetMetadata(typeInfo.Type);
            foreach (var property in typeInfo.Properties)
            {
                var member = property.AttributeProvider as PropertyInfo;
                var prop = member == null ? null : metadata.Properties.FirstOrDefault(p => p.PropertyInfo.Name == member.Name);
                if (prop == null)
                    continue;

                var existing = property.ShouldSerialize;
                property.ShouldSerialize = (owner, value) =>
                    !((ModelBase)owner).IsHiddenProperty(prop) && (existing == null || existing(owner, value));
            }
        }
    }
}
