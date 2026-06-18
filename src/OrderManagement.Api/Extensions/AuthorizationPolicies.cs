namespace OrderManagement.Api.Extensions;

public static class AuthorizationPolicies
{
    public const string AuthenticatedUser = "AuthenticatedUser";

    public const string BuyerOnly = "BuyerOnly";
    public const string BuyerOrSellerAdmin = "BuyerOrSellerAdmin";

    public const string SellerAdminOnly = "SellerAdminOnly";
    public const string SellerOperatorOnly = "SellerOperatorOnly";
    public const string SellerAdminOrOperator = "SellerAdminOrOperator";

    public const string ApplicationAdminOnly = "ApplicationAdminOnly";
    public const string DevOpsOnly = "DevOpsOnly";
    public const string ApplicationAdminOrDevOps = "ApplicationAdminOrDevOps";

    public const string StoreBackofficeUser = "StoreBackofficeUser";
    public const string InternalUser = "InternalUser";

    // Legacy constants to avoid missing policy name while refactoring older controllers.
    public const string AdminOnly = ApplicationAdminOnly;
    public const string OpsOnly = DevOpsOnly;
    public const string AdminOrOps = ApplicationAdminOrDevOps;
    public const string CustomerOnly = BuyerOnly;
    public const string CustomerOrAdmin = BuyerOrSellerAdmin;
}