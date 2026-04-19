# 🔍 Expired Assignments Analysis

**Date**: January 4, 2026  
**Issue**: Expired assignments being created when users are not logged in

---

## 🎯 **UNDERSTANDING THE PROBLEM**

### **User's Concern**:
> "I see a lot of expired assignments. The logic is supposed to be assignments are only made when a user with the right permission like analysts or audit is logged on. Why are assignments being made so they expire? There should be no assignments when users are not logged in."

### **Expected Behavior**:
- ✅ Assignments should **ONLY** be created when users with correct permissions (Analyst/Audit) are **actively logged in**
- ✅ No assignments should be created when no users are logged in
- ✅ Assignments should not expire because they shouldn't exist in the first place if users aren't logged in

---

## 🔍 **ROOT CAUSE ANALYSIS**

### **Current Assignment Flow**:

1. **AssignmentWorker runs every 5 seconds** (when `AssignmentMode == "Auto"`)
2. **Calls `AutoAssignGroupsAsync`** which:
   - Gets "ready" users via `GetReadyUsersForRoleAsync`
   - Creates assignments for those users
3. **`GetReadyUsersForRoleAsync` checks**:
   - **SignalR state** (real-time, in-memory)
   - **Database `UserReadiness` table** (persistence)
   - Users are considered "ready" if:
     - `IsReady = true` AND
     - `LastHeartbeat >= dbMaxIdle` (within last 5 minutes)

### **THE PROBLEM**:

#### **Issue #1: Stale UserReadiness Records**
- When a user logs in and sets themselves as "ready", a `UserReadiness` record is created with `IsReady = true`
- When the user logs out or their session expires, the `IsReady` flag might **still be `true`** in the database
- The `CleanupExpiredUserReadinessAsync` method runs to mark users as not ready if heartbeat expired (>5 minutes)
- **BUT**: If the heartbeat is within the 5-minute window, the user is still considered "ready" even if they're not logged in

#### **Issue #2: No Direct Authentication Check**
- The system relies on `UserReadiness` table and heartbeat mechanism
- There's **no direct check** for actual authentication/login status
- A user who logged out 4 minutes ago might still have `IsReady = true` and a recent heartbeat, so they're considered "ready"

#### **Issue #3: Heartbeat Window Too Long**
- The 5-minute heartbeat window means:
  - User logs out at 10:00 AM
  - Heartbeat was at 9:58 AM (2 minutes ago)
  - System still considers user "ready" until 10:03 AM
  - Assignments can be created for this "ready" user who is actually logged out

#### **Issue #4: Database Persistence Without Cleanup**
- The `UserReadiness` table persists across restarts
- If a user was marked as ready yesterday and never explicitly set themselves as not ready, the record might still exist
- The cleanup only marks users as not ready if heartbeat expired, but doesn't check actual login status

---

## 📊 **EVIDENCE FROM CODE**

### **AssignmentWorker.cs - Line 68-70**:
```csharp
if (assignmentMode == "Auto")
{
    await AutoAssignGroupsAsync(db, settings, now, stoppingToken);
}
```
- Runs every 5 seconds when in Auto mode
- No check for actual logged-in users

### **AssignmentWorker.cs - Line 637-731 (GetReadyUsersForRoleAsync)**:
```csharp
// Check SignalR state FIRST (real-time, immediate updates)
var signalRReadyUsers = UserReadinessStateProvider.GetReadyUsers(roleName, maxIdleTime);

// Also check database (for persistence and users not in SignalR)
var allReadinessRecords = await db.UserReadiness
    .Where(r => r.Role.ToUpper() == roleNameUpper)
    .Select(r => new { r.Username, r.IsReady, r.LastHeartbeat })
    .ToListAsync(ct);

var dbReadyUsers = allReadinessRecords
    .Where(r => r.IsReady && r.LastHeartbeat >= dbMaxIdle)  // ⚠️ Only checks heartbeat, not actual login
    .Select(r => r.Username)
    .Distinct()
    .ToList();
```
- Checks `IsReady` flag and heartbeat
- **Does NOT check if user is actually authenticated/logged in**

### **AssignmentWorker.cs - Line 878-905 (CleanupExpiredUserReadinessAsync)**:
```csharp
const int maxIdleMinutes = 5;
var maxIdleTime = now.AddMinutes(-maxIdleMinutes);

var expiredReadiness = await db.UserReadiness
    .Where(r => r.IsReady && r.LastHeartbeat < maxIdleTime)  // ⚠️ Only expires if heartbeat > 5 minutes old
    .ToListAsync(ct);
```
- Only marks users as not ready if heartbeat expired (>5 minutes)
- **Does NOT check actual login status**

---

## ✅ **PROPOSED SOLUTION**

### **Option 1: Add Authentication Status Check** (Recommended)
- Before creating assignments, verify users are actually authenticated
- Check ASP.NET Core authentication state or session validity
- Only create assignments for users who are currently logged in

### **Option 2: Reduce Heartbeat Window**
- Reduce heartbeat window from 5 minutes to 1-2 minutes
- Faster detection of logged-out users
- Still has the same fundamental issue (stale records)

### **Option 3: Add Login/Logout Tracking**
- Track when users actually log in/out
- Clear `UserReadiness` records on logout
- Only create assignments for users with active sessions

### **Option 4: Hybrid Approach** (Best)
- Combine authentication check + UserReadiness check
- Only create assignments if:
  1. User is authenticated (logged in)
  2. User has `IsReady = true` in UserReadiness
  3. User's heartbeat is recent (< 2 minutes)
  4. User has correct role permissions

---

## 🔧 **RECOMMENDED FIX**

### **Changes Needed**:

1. **Add Authentication Check in `GetReadyUsersForRoleAsync`**:
   - Verify users are actually authenticated before considering them "ready"
   - This requires access to authentication state (might need to pass it from API layer)

2. **Reduce Heartbeat Window**:
   - Change from 5 minutes to 1-2 minutes for faster detection

3. **Clear UserReadiness on Logout**:
   - When users log out, explicitly set `IsReady = false` and clear heartbeat
   - This ensures no stale records

4. **Add Logout Event Handler**:
   - Listen for logout events and update UserReadiness accordingly

---

## 📝 **NEXT STEPS**

1. ✅ **Analysis Complete** - Root cause identified
2. ⏳ **Awaiting User Approval** - No implementation without approval
3. ⏳ **Proposed Solution** - Hybrid approach recommended
4. ⏳ **Implementation** - After approval

---

## 🎯 **SUMMARY**

### **Root Cause**:
- System relies on `UserReadiness` table with heartbeat mechanism
- No direct check for actual authentication/login status
- 5-minute heartbeat window allows stale records to be considered "ready"
- Assignments created for users who are not actually logged in

### **Impact**:
- Assignments created when no users are logged in
- Assignments expire because users aren't there to work on them
- Wasted assignments and potential confusion

### **Solution**:
- Add authentication status check before creating assignments
- Reduce heartbeat window for faster detection
- Clear UserReadiness records on logout
- Only create assignments for actively authenticated users

---

**Analysis complete. Awaiting approval for implementation.** 🚀

