namespace StoreFlow.Application.Common;

/// <summary>
/// A request that produces a result. Commands carry the caller's intent and nothing else: no
/// behaviour, no persistence, no UI concerns.
/// </summary>
/// <typeparam name="TResult">The type the command produces.</typeparam>
public interface ICommand<TResult>
{
}

/// <summary>
/// A query that reads data and produces a result, without changing anything.
/// </summary>
/// <typeparam name="TResult">The type the query produces.</typeparam>
public interface IQuery<TResult>
{
}

/// <summary>
/// Handles one command. Every handler authorises first, validates second, and commits exactly one
/// transaction (PRD §8.1).
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Executes the command.</summary>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles one query.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Executes the query.</summary>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
