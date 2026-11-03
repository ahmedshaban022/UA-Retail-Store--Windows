using System.Reflection;
using StoreFlow.Desktop.ViewModels;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The till must not need the network (PRD §2.3, UAT-17).
/// </summary>
/// <remarks>
/// The till is the screen a shop depends on most. It has to work with the shop's internet
/// unplugged, so the screen the cashier uses must be made of nothing that reaches the network.
/// This phase adds the product's first code that does reach the network — the cloud backup
/// target — so the rule has to be kept rather than merely observed.
/// </remarks>
public sealed class TillNetworkTests
{
    [Fact]
    public void The_desktop_assembly_never_references_the_network()
    {
        List<string> networkReferences = typeof(TillNetworkTests).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsNetworkAssembly)
            .ToList();

        networkReferences.ShouldBeEmpty(
            "The desktop assembly must not reference the network, but it references " +
            string.Join(", ", networkReferences) + ".");
    }

    [Fact]
    public void The_till_is_made_of_no_network_client()
    {
        HashSet<Type> closure = Closure.Collect(typeof(PosViewModel));

        closure.ShouldNotBeEmpty();

        List<string> offenders = closure
            .Where(IsNetworkType)
            .Select(type => $"{type.FullName} ({type.Assembly.GetName().Name})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "The till must not contain a network client, but it contains: " + string.Join(", ", offenders));
    }

    private static bool IsNetworkAssembly(string name) =>
        name.StartsWith("System.Net", StringComparison.Ordinal) ||
        name.StartsWith("Amazon", StringComparison.Ordinal) ||
        name.StartsWith("AWSSDK", StringComparison.Ordinal) ||
        name.StartsWith("RestSharp", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);

    private static bool IsNetworkType(Type type)
    {
        if (IsNetworkAssembly(type.Assembly.GetName().Name ?? string.Empty))
        {
            return true;
        }

        return string.Equals(type.Name, "HttpClient", StringComparison.Ordinal) ||
               string.Equals(type.Name, "WebRequest", StringComparison.Ordinal) ||
               string.Equals(type.Name, "Socket", StringComparison.Ordinal);
    }

    /// <summary>
    /// The types a type is made of. The walk follows the shop's own types and stops at the
    /// framework's.
    /// </summary>
    internal static class Closure
    {
        public static HashSet<Type> Collect(Type root)
        {
            var seen = new HashSet<Type>();
            var queue = new Queue<Type>();

            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Type type = queue.Dequeue();

                if (!seen.Add(type))
                {
                    continue;
                }

                foreach (Type member in Members(type))
                {
                    if (!seen.Contains(member))
                    {
                        queue.Enqueue(member);
                    }
                }
            }

            return seen;
        }

        private static IEnumerable<Type> Members(Type type)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

            if (type.BaseType is not null)
            {
                yield return type.BaseType;
            }

            foreach (Type contract in type.GetInterfaces())
            {
                yield return contract;
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    yield return argument;
                }
            }

            foreach (FieldInfo field in type.GetFields(Flags))
            {
                yield return field.FieldType;
            }

            foreach (PropertyInfo property in type.GetProperties(Flags))
            {
                yield return property.PropertyType;
            }

            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                yield return method.ReturnType;

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(Flags))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    yield return parameter.ParameterType;
                }
            }
        }
    }
}
