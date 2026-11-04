using Microsoft.Extensions.DependencyInjection;
using KitX.WorkflowV6.Hosting;

namespace KitX.ToolKit.Builtin.Functions;

/// <summary>
/// Registers this assembly's builtin functions from <see cref="BuiltinRegistry"/>.
/// </summary>
/// <remarks>
/// <para>Internal on purpose (ADR-0011 D2): the host calls <c>AddKitXToolKit</c> and gets these
/// registered as a side effect. Nothing outside this assembly needs to name a descriptor type, so
/// the descriptors themselves stay <c>internal</c> and never reach the visible surface.</para>
/// <para>This is the only place that turns <see cref="BuiltinRegistry.All"/> into registrations.
/// There is no second list: adding a descriptor to the registry is what makes it appear in the BP
/// palette, and <c>BuiltinDescriptorAlignmentTests</c> is what makes a descriptor without an
/// implementation fail the build rather than fail at run time.</para>
/// </remarks>
internal static class BuiltinRegistrationExtensions
{
    /// <summary>Registers every builtin in <see cref="BuiltinRegistry"/>.</summary>
    public static IServiceCollection AddToolKitBuiltins(this IServiceCollection services)
    {
        foreach (var type in BuiltinRegistry.All)
            services.AddSingleton<KitX.WorkflowV6.Builtin.IBuiltinFunction>(
                sp => (KitX.WorkflowV6.Builtin.IBuiltinFunction)ActivatorUtilities.CreateInstance(sp, type));

        return services;
    }
}
