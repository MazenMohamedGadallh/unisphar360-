namespace UniSphere.Api.Services;

public enum UserType
{
    Student = 1,
    Graduate = 2,
    Doctor = 3
}

public static class RegistrationRules
{
    public const int MaxAge = 100;

    public static int GetMinAge(UserType type) => type switch
    {
        UserType.Student => 18,
        UserType.Doctor => 25,
        UserType.Graduate => 22,
        _ => 18
    };

    public static string MinAgeMessage(UserType type) =>
        $"Minimum age for a {type} is {GetMinAge(type)}.";

    public static bool TryParseUserType(string? role, out UserType type)
    {
        type = default;
        return Enum.TryParse(role?.Trim(), ignoreCase: true, out type)
            && Enum.IsDefined(typeof(UserType), type);
    }

    public static Models.UserRole ToUserRole(UserType type) => type switch
    {
        UserType.Graduate => Models.UserRole.Graduate,
        UserType.Doctor => Models.UserRole.Doctor,
        _ => Models.UserRole.Student
    };
}
