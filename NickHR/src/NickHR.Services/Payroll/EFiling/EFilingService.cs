using System.Text;
using Microsoft.EntityFrameworkCore;
using NickHR.Infrastructure.Data;

namespace NickHR.Services.Payroll.EFiling;

public class EFilingService
{
    private readonly NickHRDbContext _db;

    public EFilingService(NickHRDbContext db)
    {
        _db = db;
    }

    /// <summary>Generate SSNIT e-filing CSV for a given month/year.</summary>
    public async Task<byte[]> GenerateSSNITEFileAsync(int month, int year)
    {
        var run = await _db.PayrollRuns
            .Include(r => r.PayrollItems).ThenInclude(i => i.Employee)
            .FirstOrDefaultAsync(r => r.PayPeriodMonth == month && r.PayPeriodYear == year)
            ?? throw new InvalidOperationException($"No payroll run found for {month}/{year}.");

        var sb = new StringBuilder();
        sb.AppendLine("EmployerSSNITNumber,EmployeeName,SSNITNumber,BasicSalary,EmployeeContrib,EmployerContrib");

        // Get employer SSNIT number from company settings
        var employerSsnit = await _db.CompanySettings
            .Where(c => c.Key == "EmployerSSNITNumber")
            .Select(c => c.Value)
            .FirstOrDefaultAsync() ?? "EMPLOYER_SSNIT";

        foreach (var item in run.PayrollItems.OrderBy(i => i.Employee.LastName))
        {
            var emp = item.Employee;
            var name = $"{emp.LastName} {emp.FirstName}";
            sb.AppendLine($"{employerSsnit},{name},{emp.SSNITNumber ?? ""},{item.BasicSalary:F2},{item.SSNITEmployee:F2},{item.SSNITEmployer:F2}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Generate GRA PAYE monthly e-filing CSV.</summary>
    public async Task<byte[]> GenerateGRAEFileAsync(int month, int year)
    {
        var run = await _db.PayrollRuns
            .Include(r => r.PayrollItems).ThenInclude(i => i.Employee)
            .FirstOrDefaultAsync(r => r.PayPeriodMonth == month && r.PayPeriodYear == year)
            ?? throw new InvalidOperationException($"No payroll run found for {month}/{year}.");

        var sb = new StringBuilder();
        sb.AppendLine("TIN,EmployeeName,GrossIncome,TaxableIncome,TAXDeducted");

        foreach (var item in run.PayrollItems.OrderBy(i => i.Employee.LastName))
        {
            var emp = item.Employee;
            var name = $"{emp.LastName} {emp.FirstName}";
            sb.AppendLine($"{emp.TIN ?? ""},{name},{item.GrossPay:F2},{item.TaxableIncome:F2},{item.PAYE:F2}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Generate GRA annual return CSV for a full year.</summary>
    public async Task<byte[]> GenerateGRAAnnualEFileAsync(int year)
    {
        var runs = await _db.PayrollRuns
            .Where(r => r.PayPeriodYear == year)
            .Include(r => r.PayrollItems).ThenInclude(i => i.Employee)
            .OrderBy(r => r.PayPeriodMonth)
            .ToListAsync();

        if (!runs.Any())
            throw new InvalidOperationException($"No payroll runs found for year {year}.");

        // Aggregate by employee across all months
        var employeeAgg = new Dictionary<int, (string TIN, string Name, decimal TotalGross, decimal TotalTaxable, decimal TotalTax)>();

        foreach (var run in runs)
        {
            foreach (var item in run.PayrollItems)
            {
                var emp = item.Employee;
                if (!employeeAgg.ContainsKey(emp.Id))
                    employeeAgg[emp.Id] = (emp.TIN ?? "", $"{emp.LastName} {emp.FirstName}", 0, 0, 0);

                var agg = employeeAgg[emp.Id];
                employeeAgg[emp.Id] = (agg.TIN, agg.Name,
                    agg.TotalGross + item.GrossPay,
                    agg.TotalTaxable + item.TaxableIncome,
                    agg.TotalTax + item.PAYE);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("TIN,EmployeeName,AnnualGrossIncome,AnnualTaxableIncome,AnnualTAXDeducted");

        foreach (var kvp in employeeAgg.OrderBy(k => k.Value.Name))
        {
            var v = kvp.Value;
            sb.AppendLine($"{v.TIN},{v.Name},{v.TotalGross:F2},{v.TotalTaxable:F2},{v.TotalTax:F2}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
