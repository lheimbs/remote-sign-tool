using System;
using Microsoft.Extensions.DependencyInjection;

namespace RemoteSignTool.Server;

/// <summary>
/// Provides a simple implementation of IServiceProvider using the Microsoft Dependency Injection framework.
/// </summary>
public class SimpleIocResolver : IServiceProvider
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleIocResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public SimpleIocResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Resolves the specified service type.
    /// </summary>
    /// <param name="serviceType">The type of service to resolve.</param>
    /// <returns>An instance of the service, or null if it cannot be resolved.</returns>
    public object? GetService(Type serviceType)
    {
        return _serviceProvider.GetService(serviceType);
    }
}
