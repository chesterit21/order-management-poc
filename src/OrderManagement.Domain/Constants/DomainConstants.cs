namespace OrderManagement.Domain.Constants;

public static class DomainConstants
{
    public const int MaxSkuLength = 100;
    public const int MaxProductNameLength = 200;
    public const int MaxUsernameLength = 100;
    public const int MaxDisplayNameLength = 150;
    public const int MaxOrderNumberLength = 50;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxEndpointLength = 200;
    public const int MaxPaymentProviderLength = 100;
    public const int MaxPaymentReferenceLength = 200;

    public const int MaxStoreNameLength = 150;
    public const int MaxStoreSlugLength = 160;
    public const int MaxStoreDescriptionLength = 1000;

    public const string OrderNumberPrefix = "ORD";
}