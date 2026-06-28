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
    protected Application(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        App.ServiceProviderFactory ??= () => (ApplicationContext ?? this).CreateNoctraAndroidServiceProvider();
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
