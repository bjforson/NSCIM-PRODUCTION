-- Quick Check: Users with Analyst Role (CORRECTED)
-- This uses the correct table names: Users and Roles (not AspNetUsers/AspNetRoles)

SELECT 
    u.Username,
    u.Email,
    r.Name AS RoleName,
    u.IsActive AS UserActive,
    r.IsActive AS RoleActive
FROM Users u
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst'
    AND u.IsActive = 1
    AND r.IsActive = 1
ORDER BY u.Username;

