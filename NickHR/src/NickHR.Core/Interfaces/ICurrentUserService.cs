namespace NickHR.Core.Interfaces;

public interface ICurrentUserService
{
    string UserId { get; }
    string UserName { get; }
    int? EmployeeId { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}
