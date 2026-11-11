using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Theming;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Watches the wiring between the screens and the handlers behind them.
/// </summary>
/// <remarks>
/// <para>
/// The suite used to call the handlers one at a time. That proves a handler works, and says
/// nothing at all about whether a screen can reach it: an orphaned handler resolves perfectly,
/// passes every test that drives it directly, and leaves the shop waiting for a decision that
/// nothing will ever make. That is exactly how the approved stock adjustment was lost for a
/// release — the handler was green and no screen asked for it.
/// </para>
/// <para>
/// So this test reads the composition root and asks, of every command the shop registers, whether
/// a screen asks for that command. The commands that are reached from somewhere else instead — the
/// scheduler, which is a service and not a screen — are named in the list below, with the reason
/// they are allowed to stand alone.
/// </para>
/// </remarks>
public sealed class HandlerWiringTests
{
    /// <summary>
    /// The commands that no screen asks for, and why each one is allowed to stand alone.
    /// </summary>
    private static readonly Dictionary<string, string> ReachedWithoutAScreen = new()
    {
        ["RunScheduledBackupsCommand"] = "the backup scheduler runs it on a timer",
    };

    [Fact]
    public void Every_command_the_shop_registers_is_reached_from_a_screen()
    {
        List<string> orphans = FindOrphans(CompositionRoot());

        orphans.ShouldBeEmpty(
            "these commands are registered but no screen asks for them, so nothing in the shop can "
            + "run them: " + string.Join(", ", orphans));
    }

    [Fact]
    public void A_handler_that_no_screen_asks_for_is_found()
    {
        // A guard that cannot fail is not a guard. So the guard is given a handler for a command
        // that no screen asks for — an orphan of exactly the kind it exists to find — and it has
        // to name it.
        IServiceCollection services = CompositionRoot();

        services.AddSingleton<ICommandHandler<OrphanCommand, Result>, OrphanHandler>();

        FindOrphans(services).ShouldContain(nameof(OrphanCommand));
    }

    [Fact]
    public void The_list_of_commands_reached_without_a_screen_is_still_needed()
    {
        // The allow-list is a list of exceptions, and an exception nobody needs any more is just
        // another way for an orphan to slip through. Every name in it must still be a command the
        // shop registers, and the list must stay short enough to be read.
        HashSet<Type> registered = RegisteredCommands(CompositionRoot());

        ReachedWithoutAScreen.Count.ShouldBeLessThanOrEqualTo(5);

        foreach (string name in ReachedWithoutAScreen.Keys)
        {
            registered.ShouldContain(command => command.Name == name);
        }
    }

    /// <summary>The composition root as the shop builds it.</summary>
    /// <returns>The services the application registers.</returns>
    private static IServiceCollection CompositionRoot()
    {
        var services = new ServiceCollection();

        // The paths are only read by the registrations, and nothing is written: the test asks what
        // is registered, not what the shop does.
        services.AddStoreFlowServices(new AppPaths(), BuiltInThemes.Light);

        return services;
    }

    /// <summary>The commands the container holds a handler for.</summary>
    /// <param name="services">The services to read.</param>
    /// <returns>The command types.</returns>
    private static HashSet<Type> RegisteredCommands(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .ToHashSet();

    /// <summary>The commands that are registered but that no screen asks for.</summary>
    /// <param name="services">The services to read.</param>
    /// <returns>The names of the orphaned commands.</returns>
    private static List<string> FindOrphans(IServiceCollection services)
    {
        HashSet<Type> registered = RegisteredCommands(services);
        HashSet<Type> askedFor = CommandsAskedForByScreens();

        // A reflection that suddenly found nothing would pass the guard for the wrong reason, so
        // the guard checks that it read the shop before it judges it.
        registered.Count.ShouldBeGreaterThan(30);
        askedFor.Count.ShouldBeGreaterThan(30);

        return registered
            .Where(command => !askedFor.Contains(command))
            .Select(command => command.Name)
            .Where(name => !ReachedWithoutAScreen.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The commands the screens ask for.</summary>
    /// <returns>The command types.</returns>
    private static HashSet<Type> CommandsAskedForByScreens()
    {
        var asked = new HashSet<Type>();

        foreach (Type viewModel in typeof(ApprovalsViewModel).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                && type.IsPublic
                && type.Name.EndsWith("ViewModel", StringComparison.Ordinal)))
        {
            // A screen asks for a command by holding the handler for it, which it is given in its
            // constructor and keeps in a field.
            foreach (Type parameter in viewModel.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType))
            {
                AddCommand(parameter, asked);
            }

            foreach (FieldInfo field in viewModel.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                AddCommand(field.FieldType, asked);
            }
        }

        return asked;
    }

    /// <summary>Notes the command behind a handler type, when the type is a handler.</summary>
    /// <param name="type">The type to read.</param>
    /// <param name="asked">The commands collected so far.</param>
    private static void AddCommand(Type type, HashSet<Type> asked)
    {
        if (!type.IsGenericType)
        {
            return;
        }

        Type definition = type.GetGenericTypeDefinition();

        if (definition != typeof(ICommandHandler<,>))
        {
            return;
        }

        asked.Add(type.GetGenericArguments()[0]);
    }

    /// <summary>A command that no screen asks for.</summary>
    private sealed record OrphanCommand : ICommand<Result>;

    /// <summary>A handler for a command that no screen asks for.</summary>
    private sealed class OrphanHandler : ICommandHandler<OrphanCommand, Result>
    {
        /// <inheritdoc />
        public Task<Result> HandleAsync(OrphanCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }
}
