using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using GibddExamSimulator.Mobile.Shared.Services;

namespace GibddExamSimulator.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize |
                           ConfigChanges.Orientation |
                           ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize |
                           ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new ActiveSessionBackCallback(this));
    }

    protected override void OnResume()
    {
        base.OnResume();
        var state = IPlatformApplication.Current?.Services.GetService<MobileAppState>();
        if (state is not null)
            _ = state.ResumeAsync();
    }

    private sealed class ActiveSessionBackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            var state = IPlatformApplication.Current?.Services.GetService<MobileAppState>();
            if (state?.ActiveSession is { IsCompleted: false })
            {
                var dialog = new AlertDialog.Builder(activity);
                dialog.SetTitle("Активная сессия");
                dialog.SetMessage("Прогресс сохранён. Выйти с экрана и продолжить позже?");
                dialog.SetNegativeButton("Остаться", (_, _) => { });
                dialog.SetPositiveButton("Выйти", (_, _) => ContinueBack());
                _ = dialog.Show();
                return;
            }
            ContinueBack();
        }

        private void ContinueBack()
        {
            Enabled = false;
            activity.OnBackPressedDispatcher.OnBackPressed();
            Enabled = true;
        }
    }
}
