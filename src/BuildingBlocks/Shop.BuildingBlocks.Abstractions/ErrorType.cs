namespace Shop.BuildingBlocks.Abstractions;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    PreconditionFailed,
    UnprocessableEntity,
    Failure
}
