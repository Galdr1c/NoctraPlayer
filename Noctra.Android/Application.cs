using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Noctra.Android.DependencyInjection;
using Noctra.Mobile;

namespace Noctra.Android;

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    private readonly object _servicesGate = new();
    private IServiceProvider? _services;

    protected Application(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public IServiceProvider Services
    {
        get
        {
            lock (_servicesGate)
            {
                return _services ??=
                    (ApplicationContext ?? this).CreateNoctraAndroidServiceProvider();
            }
        }
    }

    public override void OnCreate()
    {
        App.ServiceProviderFactory ??= () => Services;
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
