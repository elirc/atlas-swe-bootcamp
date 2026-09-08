using Atlas.Domain.Entities;

namespace Atlas.Api.Endpoints;

internal static class ExpenseInput
{
    public static Dictionary<string, string[]> Validate(string? description, List<CreateExpenseItemRequest>? items)
    {
        var errors = new Dictionary<string, string[]>();
        if (description?.Trim().Length > 500) errors["description"] = ["Description must contain at most 500 characters."];
        if (items is not { Count: >= 1 and <= 50 }) { errors["items"] = ["Supply 1–50 expense items."]; return errors; }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null) { errors[$"items[{i}]"] = ["An expense item cannot be null."]; continue; }
            if (string.IsNullOrWhiteSpace(item.Description) || item.Description.Trim().Length > 300) errors[$"items[{i}].description"] = ["Description must contain 1–300 characters."];
            if (item.Amount <= 0) errors[$"items[{i}].amount"] = ["Amount must be greater than zero."];
            if (item.IncurredDate.Year is < 2000 or > 2100) errors[$"items[{i}].incurredDate"] = ["Use an incurred date with year 2000–2100."];
            if (!string.IsNullOrWhiteSpace(item.ReceiptUrl) && (item.ReceiptUrl.Trim().Length > 2000
                || !Uri.TryCreate(item.ReceiptUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                errors[$"items[{i}].receiptUrl"] = ["Receipt URL must be an absolute HTTP(S) URL of at most 2,000 characters."];
        }
        if (errors.Count == 0)
        {
            try { _ = items.Sum(x => x.Amount); }
            catch (OverflowException) { errors["items"] = ["The combined amount exceeds supported decimal arithmetic."]; }
        }
        return errors;
    }
    public static List<ExpenseItem> Items(Guid claimId, List<CreateExpenseItemRequest> items) => items.Select(x => new ExpenseItem
    {
        ExpenseClaimId = claimId, Description = x.Description!.Trim(), Amount = x.Amount, IncurredDate = x.IncurredDate,
        ReceiptUrl = string.IsNullOrWhiteSpace(x.ReceiptUrl) ? null : x.ReceiptUrl.Trim(),
    }).ToList();
}
