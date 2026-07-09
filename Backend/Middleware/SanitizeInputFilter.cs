using Microsoft.AspNetCore.Mvc.Filters;
using System.Reflection;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Middleware;

public class SanitizeInputFilter : IAsyncActionFilter
{
    private readonly ILogger<SanitizeInputFilter> _logger;

    public SanitizeInputFilter(ILogger<SanitizeInputFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument != null)
            {
                SanitizeObject(argument);
            }
        }

        await next();
    }

    private void SanitizeObject(object obj)
    {
        if (obj == null) return;
        
        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string)) return;
        
        // Handle IEnumerable
        if (obj is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                SanitizeObject(item);
            }
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

        foreach (var prop in properties)
        {
            if (prop.GetCustomAttribute<SkipSanitizationAttribute>() != null)
                continue;

            if (prop.PropertyType == typeof(string))
            {
                var value = (string?)prop.GetValue(obj);
                if (value != null)
                {
                    var sanitized = SanitizationHelper.SanitizeString(value);
                    if (value != sanitized)
                    {
                        prop.SetValue(obj, sanitized);
                        _logger.LogDebug("Sanitized property {Property} on type {Type}", prop.Name, type.Name);
                    }
                }
            }
            else if (!prop.PropertyType.IsPrimitive && !prop.PropertyType.IsEnum)
            {
                var nestedObj = prop.GetValue(obj);
                if (nestedObj != null)
                {
                    SanitizeObject(nestedObj);
                }
            }
        }
    }
}
