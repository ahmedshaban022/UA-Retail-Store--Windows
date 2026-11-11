using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Shouldly;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Checks that the language service does not keep the screens alive, and that a window stops
/// following the language once it has been closed.
/// </summary>
/// <remarks>
/// A screen is built fresh every time the owner opens it, and the language service lives for as
/// long as the programme does. If the service holds its listeners strongly, every screen the
/// owner has ever opened stays alive for the rest of the session, and every change of language
/// goes on refreshing screens nobody is looking at.
/// </remarks>
public sealed class LanguageSubscriptionTests
{
    [Fact]
    public void A_screen_that_nothing_refers_to_any_more_is_not_kept_alive_by_the_service()
    {
        var language = new LanguageService();

        WeakReference reference = SubscribeAndLetGo(language);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.IsAlive.ShouldBeFalse(
            "The language service was still holding a screen that nothing refers to any more.");
    }

    [Fact]
    public void A_screen_that_is_still_there_is_told_about_the_change()
    {
        var language = new LanguageService();
        var listener = new CountingListener();

        language.LanguageChanged += listener.OnLanguageChanged;

        language.SetLanguage("en");

        listener.Count.ShouldBe(1);
    }

    [Fact]
    public void A_screen_that_has_taken_itself_off_is_not_told_about_the_change()
    {
        var language = new LanguageService();
        var listener = new CountingListener();

        language.LanguageChanged += listener.OnLanguageChanged;
        language.LanguageChanged -= listener.OnLanguageChanged;

        language.SetLanguage("en");

        listener.Count.ShouldBe(0);
    }

    [Fact]
    public void A_window_stops_following_the_language_when_it_is_closed()
    {
        RunOnAStaThread(() =>
        {
            var language = new LanguageService();
            language.SetLanguage("ar");

            var window = new Window { Width = 200, Height = 100 };
            window.Show();

            WindowDirection.Watch(window, language);

            window.FlowDirection.ShouldBe(FlowDirection.RightToLeft);

            language.SetLanguage("en");
            window.FlowDirection.ShouldBe(FlowDirection.LeftToRight);

            window.Close();

            // The window is closed, so the language no longer has anything to say to it. If the
            // closed window were still listening, it would be redrawn for the rest of the
            // session and would keep everything it holds alive with it.
            language.SetLanguage("ar");

            window.FlowDirection.ShouldBe(FlowDirection.LeftToRight);
        });
    }

    /// <summary>
    /// Subscribes a listener and lets go of it, so that only the service is left holding it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeAndLetGo(LanguageService language)
    {
        var listener = new CountingListener();
        language.LanguageChanged += listener.OnLanguageChanged;

        return new WeakReference(listener);
    }

    private sealed class CountingListener
    {
        public int Count { get; private set; }

        public void OnLanguageChanged(object? sender, EventArgs e) => Count++;
    }

    private static void RunOnAStaThread(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // The failure has to travel back to the test thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
