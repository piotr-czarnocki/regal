namespace Regal.Web.Features.Audit;

public static class AuditActions
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string AccessDenied = "AccessDenied";
    public const string EntityCreated = "EntityCreated";
    public const string EntityUpdated = "EntityUpdated";
    public const string EntityDeactivated = "EntityDeactivated";
    public const string EntityActivated = "EntityActivated";
    public const string StockDocumentPosted = "StockDocumentPosted";
    public const string StockDocumentReversed = "StockDocumentReversed";
    public const string InvoiceIssued = "InvoiceIssued";
    public const string InvoiceSent = "InvoiceSent";
    public const string InvoiceSendFailed = "InvoiceSendFailed";
    public const string InvoiceRetried = "InvoiceRetried";
    public const string InvoiceCancelled = "InvoiceCancelled";
    public const string InvoiceCorrected = "InvoiceCorrected";
    public const string InvoiceEmailed = "InvoiceEmailed";
    public const string OrderCreated = "OrderCreated";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderCompleted = "OrderCompleted";
}
