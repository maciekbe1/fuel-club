namespace FuelClub.Api.Modules.Loyalty;

public static class LoyaltyErrorCodes
{
    public const string RequiredField = "loyalty.required_field";
    public const string TooLong = "loyalty.too_long";
    public const string InvalidFormat = "loyalty.invalid_format";
    public const string AmountOutOfRange = "loyalty.amount_out_of_range";
    public const string AmountPrecision = "loyalty.amount_precision";
    public const string LitersOutOfRange = "loyalty.liters_out_of_range";
    public const string LitersPrecision = "loyalty.liters_precision";
    public const string SaleDateInFuture = "loyalty.sale_date_in_future";
    public const string CashierStationMissing = "loyalty.cashier_station_missing";
    public const string CardMissing = "loyalty.card_missing";
    public const string CardAlreadyIssued = "loyalty.card_already_issued";
    public const string CustomerBusy = "loyalty.customer_busy";
    public const string CardNotFound = "loyalty.card_not_found";
    public const string ReceiptAlreadyRegistered = "loyalty.receipt_already_registered";
    public const string ValidationFailed = "loyalty.validation_failed";
    public const string LimitOutOfRange = "loyalty.limit_out_of_range";
    public const string InvalidCursor = "loyalty.invalid_cursor";
}
