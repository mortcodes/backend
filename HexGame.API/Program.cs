using HexGame.API.Services;
using HexGame.API.Data;
using HexGame.API.Models;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Postgrest.Attributes;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.MaxDepth = 64;
        
        // Add a converter to ignore Postgrest attributes
        options.JsonSerializerOptions.Converters.Add(new PostgrestAttributeIgnoringConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HexGame API", Version = "v1" });
});

// Configure Supabase service
builder.Services.AddSingleton<ISupabaseService>(sp => 
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new SupabaseService(
        configuration["Supabase:Url"] ?? throw new InvalidOperationException("Supabase URL is required"),
        configuration["Supabase:Key"] ?? throw new InvalidOperationException("Supabase Key is required")
    );
});

// Configure game services
builder.Services.AddSingleton<IGameRepository, SupabaseGameRepository>();
builder.Services.AddSingleton<IGameService, GameService>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();

// Add a custom JSON converter to properly ignore Postgrest attributes
public class PostgrestAttributeIgnoringConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Check if the type has any properties with Postgrest attributes
        return typeToConvert.GetProperties()
            .Any(p => p.GetCustomAttributes()
                .Any(a => a.GetType().Namespace?.StartsWith("Postgrest.Attributes") == true));
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(PostgrestAttributeIgnoringJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private class PostgrestAttributeIgnoringJsonConverter<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Create a new JsonSerializerOptions without this converter to avoid infinite recursion
            var newOptions = new JsonSerializerOptions(options);
            
            // Create a new converter list without this converter
            var filteredConverters = options.Converters.Where(c => !(c is PostgrestAttributeIgnoringConverter)).ToList();
            newOptions.Converters.Clear();
            foreach (var converter in filteredConverters)
            {
                newOptions.Converters.Add(converter);
            }
            
            // Use the default deserialization for reading
            return JsonSerializer.Deserialize<T>(ref reader, newOptions);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();

            // Get all properties of the type
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Create a new JsonSerializerOptions without this converter to avoid infinite recursion
            var newOptions = new JsonSerializerOptions(options);
            
            // Create a new converter list without this converter
            var filteredConverters = options.Converters.Where(c => !(c is PostgrestAttributeIgnoringConverter)).ToList();
            newOptions.Converters.Clear();
            foreach (var converter in filteredConverters)
            {
                newOptions.Converters.Add(converter);
            }

            // Write only properties that don't have Postgrest attributes
            foreach (var property in properties)
            {
                // Skip properties with Postgrest attributes
                if (property.GetCustomAttributes()
                    .Any(a => a.GetType().Namespace?.StartsWith("Postgrest.Attributes") == true))
                {
                    continue;
                }

                // Write property name
                writer.WritePropertyName(newOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name);

                // Get property value
                var propertyValue = property.GetValue(value);

                // Serialize the property value
                JsonSerializer.Serialize(writer, propertyValue, newOptions);
            }

            writer.WriteEndObject();
        }
    }
}
