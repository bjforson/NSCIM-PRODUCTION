using Microsoft.EntityFrameworkCore;
using NickHR.Core.Entities.Core;
using NickHR.Infrastructure.Data;

namespace NickHR.Services.Succession;

public class SuccessionService
{
    private readonly NickHRDbContext _db;

    public SuccessionService(NickHRDbContext db)
    {
        _db = db;
    }

    public async Task<List<SuccessionPlan>> GetAllPlansAsync()
    {
        return await _db.Set<SuccessionPlan>()
            .Include(s => s.Designation)
            .Include(s => s.IncumbentEmployee)
            .Include(s => s.Candidates).ThenInclude(c => c.CandidateEmployee)
            .OrderBy(s => s.Priority == "Critical" ? 0 : s.Priority == "High" ? 1 : s.Priority == "Medium" ? 2 : 3)
            .ToListAsync();
    }

    public async Task<SuccessionPlan?> GetByIdAsync(int id)
    {
        return await _db.Set<SuccessionPlan>()
            .Include(s => s.Designation)
            .Include(s => s.IncumbentEmployee)
            .Include(s => s.Candidates).ThenInclude(c => c.CandidateEmployee)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SuccessionPlan> CreatePlanAsync(SuccessionPlan plan)
    {
        _db.Set<SuccessionPlan>().Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    public async Task<SuccessionPlan> UpdatePlanAsync(SuccessionPlan plan)
    {
        _db.Set<SuccessionPlan>().Update(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    public async Task<SuccessionCandidate> AddCandidateAsync(SuccessionCandidate candidate)
    {
        _db.Set<SuccessionCandidate>().Add(candidate);
        await _db.SaveChangesAsync();
        return candidate;
    }

    public async Task<SuccessionCandidate> UpdateCandidateAsync(SuccessionCandidate candidate)
    {
        _db.Set<SuccessionCandidate>().Update(candidate);
        await _db.SaveChangesAsync();
        return candidate;
    }

    public async Task RemoveCandidateAsync(int candidateId)
    {
        var candidate = await _db.Set<SuccessionCandidate>().FindAsync(candidateId)
            ?? throw new KeyNotFoundException("Candidate not found.");

        candidate.IsDeleted = true;
        await _db.SaveChangesAsync();
    }
}
