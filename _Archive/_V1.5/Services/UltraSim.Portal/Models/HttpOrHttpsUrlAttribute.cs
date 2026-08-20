using System.ComponentModel.DataAnnotations;

namespace UltraSim.Portal.Models;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
internal sealed class HttpOrHttpsUrlAttribute : ValidationAttribute
{
    public HttpOrHttpsUrlAttribute()
        : base("Must be a valid http or https URL.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return true;

        return Uri.TryCreate(s.Trim(), UriKind.Absolute, out Uri? uri) &&
               (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}
