using System.Text.Json;
using System.Text;
using xKsef.Contracts.SalesInvoices;

namespace xKsef.Tests;

internal static class SalesInvoiceSnapshotTestData
{
    internal static xKsef.Domain.SalesInvoices.InvoiceQualificationsData ConfirmedQualifications()
    {
        var result = new xKsef.Domain.SalesInvoices.InvoiceQualificationsData();
        foreach (var property in result.GetType().GetProperties()) property.SetValue(result, new xKsef.Domain.SalesInvoices.QualificationDecisionData
            { Value = xKsef.Domain.SalesInvoices.QualificationValue.No, Source = xKsef.Domain.SalesInvoices.QualificationSource.UserConfirmed });
        return result;
    }
    internal static InvoiceQualificationsDto ConfirmedQualificationsDto()
    {
        var result = new InvoiceQualificationsDto();
        foreach (var property in result.GetType().GetProperties()) property.SetValue(result, new QualificationDecisionDto(1, null, true));
        return result;
    }
}
