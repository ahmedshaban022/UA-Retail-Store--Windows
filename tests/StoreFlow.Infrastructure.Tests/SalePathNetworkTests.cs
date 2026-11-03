using System.Reflection;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Infrastructure.Pos;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The sale path must not need the network (PRD §2.3, UAT-17).
/// </summary>
/// <remarks>
/// <para>The shop's data lives in one local file. A sale that had to ask a server whether it could
/// go ahead would stop working the moment the shop's internet did, and a shop that cannot take a
/// sale cannot trade. The whole product is offline for that reason.</para>
/// <para>This phase adds the first code in the product that does reach the network — the cloud
/// backup target — so the rule now has to be kept rather than merely observed. The test walks the
/// types the sale path is made of and refuses any of them that is a network client.</para>
/// </remarks>
public sealed class SalePathNetworkTests
{
    [Fact]
    public void The_core_of_the_shop_never_references_the_network()
    {
        foreach (Assembly assembly in new[] { typeof(Result).Assembly, typeof(Permission).Assembly })
        {
            List<string> networkReferences = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(IsNetworkAssembly)
                .ToList();

            networkReferences.ShouldBeEmpty(
                $"{assembly.GetName().Name} must not reference the network, but it references {string.Join(", ", networkReferences)}.");
        }
    }

    [Fact]
    public void The_sale_path_is_made_of_no_network_client()
    {
        HashSet<Type> closure = Closure.Collect(typeof(PostSaleCommandHandler));

        closure.ShouldNotBeEmpty();

        List<string> offenders = closure
            .Where(IsNetworkType)
            .Select(type => $"{type.FullName} ({type.Assembly.GetName().Name})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "The sale path must not contain a network client, but it contains: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// True when an assembly belongs to the network.
    /// </summary>
    private static bool IsNetworkAssembly(string name) =>
        name.StartsWith("System.Net", StringComparison.Ordinal) ||
        name.StartsWith("Amazon", StringComparison.Ordinal) ||
        name.StartsWith("AWSSDK", StringComparison.Ordinal) ||
        name.StartsWith("RestSharp", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);

    /// <summary>
    /// True when a type is a way of reaching the network.
    /// </summary>
    private static bool IsNetworkType(Type type)
    {
        // The assembly is the honest signal: a type reaches the network by living in the
        // assembly that does the reaching. A namespace on its own is not enough — System.Text.Json
        // has a type called System.Net.ArrayBuffer, and it does not reach anywhere.
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
    /// framework's, because the framework is not the shop's to change.
    /// </summary>
    internal static class Closure
    {
        /// <summary>
        /// Every type the given type is made of.
        /// </summary>
        /// <param name="root">The type to walk from.</param>
        /// <returns>The types, including the root.</returns>
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
