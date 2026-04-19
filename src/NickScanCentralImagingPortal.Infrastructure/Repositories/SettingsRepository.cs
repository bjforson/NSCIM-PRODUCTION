using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class SettingsRepository : ISettingsRepository
    {
        private readonly ApplicationDbContext _context;

        public SettingsRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // System Settings CRUD
        public async Task<SystemSetting?> GetSettingAsync(string category, string key)
        {
            return await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Category == category && s.SettingKey == key);
        }

        public async Task<List<SystemSetting>> GetSettingsByCategoryAsync(string category)
        {
            return await _context.SystemSettings
                .Where(s => s.Category == category && s.IsActive)
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.SettingKey)
                .ToListAsync();
        }

        public async Task<List<SystemSetting>> GetAllSettingsAsync()
        {
            return await _context.SystemSettings
                .Where(s => s.IsActive)
                .OrderBy(s => s.Category)
                .ThenBy(s => s.DisplayOrder)
                .ToListAsync();
        }

        public async Task<List<string>> GetCategoriesAsync()
        {
            return await _context.SystemSettings
                .Where(s => s.IsActive)
                .Select(s => s.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
        }

        public async Task<SystemSetting> CreateSettingAsync(SystemSetting setting)
        {
            _context.SystemSettings.Add(setting);
            await _context.SaveChangesAsync();
            return setting;
        }

        public async Task<SystemSetting> UpdateSettingAsync(SystemSetting setting)
        {
            setting.UpdatedAt = DateTime.UtcNow;
            _context.SystemSettings.Update(setting);
            await _context.SaveChangesAsync();
            return setting;
        }

        public async Task<bool> DeleteSettingAsync(int id)
        {
            var setting = await _context.SystemSettings
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == id);
            if (setting == null) return false;

            _context.SystemSettings.Remove(setting);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> BulkUpdateSettingsAsync(List<SystemSetting> settings)
        {
            foreach (var setting in settings)
            {
                setting.UpdatedAt = DateTime.UtcNow;
            }

            _context.SystemSettings.UpdateRange(settings);
            return await _context.SaveChangesAsync();
        }

        // Settings History
        public async Task<SettingsHistory> AddHistoryAsync(SettingsHistory history)
        {
            _context.SettingsHistories.Add(history);
            await _context.SaveChangesAsync();
            return history;
        }

        public async Task<List<SettingsHistory>> GetHistoryAsync(int systemSettingId, int limit = 50)
        {
            return await _context.SettingsHistories
                .Where(h => h.SystemSettingId == systemSettingId)
                .OrderByDescending(h => h.ChangedAt)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<List<SettingsHistory>> GetRecentHistoryAsync(int limit = 100)
        {
            return await _context.SettingsHistories
                .OrderByDescending(h => h.ChangedAt)
                .Take(limit)
                .ToListAsync();
        }

        // User Preferences
        public async Task<UserPreference?> GetUserPreferenceAsync(int userId, string key)
        {
            return await _context.UserPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key);
        }

        public async Task<List<UserPreference>> GetAllUserPreferencesAsync(int userId)
        {
            return await _context.UserPreferences
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.PreferenceKey)
                .ToListAsync();
        }

        public async Task<UserPreference> SetUserPreferenceAsync(UserPreference preference)
        {
            var existing = await GetUserPreferenceAsync(preference.UserId, preference.PreferenceKey);

            if (existing != null)
            {
                existing.PreferenceValue = preference.PreferenceValue;
                existing.UpdatedAt = DateTime.UtcNow;
                _context.UserPreferences.Update(existing);
            }
            else
            {
                _context.UserPreferences.Add(preference);
            }

            await _context.SaveChangesAsync();
            return existing ?? preference;
        }

        public async Task<bool> DeleteUserPreferenceAsync(int userId, string key)
        {
            var preference = await GetUserPreferenceAsync(userId, key);
            if (preference == null) return false;

            _context.UserPreferences.Remove(preference);
            await _context.SaveChangesAsync();
            return true;
        }

        // Utilities
        public async Task<bool> SettingExistsAsync(string category, string key)
        {
            return await _context.SystemSettings
                .AnyAsync(s => s.Category == category && s.SettingKey == key);
        }

        public async Task<int> GetSettingsCountByCategoryAsync(string category)
        {
            return await _context.SystemSettings
                .CountAsync(s => s.Category == category && s.IsActive);
        }
    }
}

