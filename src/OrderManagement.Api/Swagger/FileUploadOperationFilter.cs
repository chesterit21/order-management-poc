using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderManagement.Api.Swagger;

public class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var parameters = context.ApiDescription.ActionDescriptor.Parameters;
        var hasFileUpload = parameters.Any(p =>
            p.ParameterType == typeof(IFormFile) ||
            (p.ParameterType?.FullName == "Microsoft.AspNetCore.Http.IFormFile"));

        if (!hasFileUpload)
        {
            hasFileUpload = context.MethodInfo.GetParameters()
                .SelectMany(p => p.ParameterType?.GetProperties() ?? [])
                .Any(prop => prop.PropertyType == typeof(IFormFile));
        }

        if (!hasFileUpload)
            return;

        operation.Parameters = [];
        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "binary",
                                Description = "The file to upload"
                            }
                        },
                        Required = new HashSet<string> { "file" }
                    }
                }
            }
        };
    }
}
