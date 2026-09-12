using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PetPlayer.Models;
using PetPlayer.ViewModels;

namespace PetPlayer.Views;

/// <summary>
/// Collapsible transcript panel. Reuses MainWindow's DataContext (MainViewModel)
/// rather than owning a separate view model, since transcript state is
/// inseparable from the current playback position.
/// </summary>
public partial class TranscriptPanelView : UserControl
{
    private static readonly TimeSpan ManualScrollGuardWindow = TimeSpan.FromSeconds(4);

    private MainViewModel? _viewModel;
    private DateTime _lastManualScrollUtc = DateTime.MinValue;
    private bool _isAutoScrolling;

    public TranscriptPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = e.NewValue as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveTranscriptEntry) || _viewModel?.ActiveTranscriptEntry is null)
        {
            return;
        }

        if (DateTime.UtcNow - _lastManualScrollUtc < ManualScrollGuardWindow)
        {
            // The user appears to be reading elsewhere in the transcript - don't
            // steal their scroll position.
            return;
        }

        _isAutoScrolling = true;
        TranscriptList.ScrollIntoView(_viewModel.ActiveTranscriptEntry);
        _isAutoScrolling = false;
    }

    private void TranscriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is not null && TranscriptList.SelectedItem is TranscriptEntry entry)
        {
            _viewModel.SeekToTranscriptEntryCommand.Execute(entry);
        }
    }

    private void TranscriptScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_isAutoScrolling && e.VerticalChange != 0)
        {
            _lastManualScrollUtc = DateTime.UtcNow;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IsTranscriptPanelVisible = false;
        }
    }
}
