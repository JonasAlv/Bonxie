using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels.Packets;

public partial class PacketLoggerViewModel : BotControlViewModelBase
{
    public PacketLoggerViewModel(IEnumerable<PacketLogFilterViewModel> filters, IFlashUtil flash, IFileDialogService fileDialog)
        : base("Packet Logger")
    {
        _flash = flash;
        _fileDialog = fileDialog;
        _allPacketFilters = filters.ToList();
        foreach (PacketLogFilterViewModel filter in _allPacketFilters)
            filter.PropertyChanged += OnFilterPropertyChanged;
    }

    private readonly IFlashUtil _flash;
    private readonly IFileDialogService _fileDialog;
    private readonly List<string> _allPacketLogs = [];
    private readonly List<PacketLogFilterViewModel> _allPacketFilters;

    [ObservableProperty]
    private ObservableCollection<string> _filteredPacketLogs = new();

    [ObservableProperty]
    private string _packetSearchText = string.Empty;

    public IReadOnlyList<PacketLogFilterViewModel> PacketFilters => _allPacketFilters;

    // "shown / total" for the filter button
    public string ShownFiltersText => $"{_allPacketFilters.Count(f => f.IsChecked)} / {_allPacketFilters.Count}";

    private bool _isReceivingPackets;

    public bool IsReceivingPackets
    {
        get => _isReceivingPackets;
        set
        {
            if (SetProperty(ref _isReceivingPackets, value))
            {
                ToggleLogger();
                OnPropertyChanged(nameof(StartStopText));
            }
        }
    }

    public string StartStopText => IsReceivingPackets ? "Stop logging" : "Start logging";

    [RelayCommand]
    private void ToggleLogging()
    {
        IsReceivingPackets = !IsReceivingPackets;
    }

    [RelayCommand]
    private void SavePacketLogs()
    {
        _fileDialog.SaveText(_allPacketLogs);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _allPacketFilters.ForEach(f => f.IsChecked = false);
    }

    [RelayCommand]
    private void SelectAllFilters()
    {
        _allPacketFilters.ForEach(f => f.IsChecked = true);
    }

    [RelayCommand]
    private void InvertFilters()
    {
        _allPacketFilters.ForEach(f => f.IsChecked = !f.IsChecked);
    }

    private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PacketLogFilterViewModel.IsChecked))
            OnPropertyChanged(nameof(ShownFiltersText));
    }

    [RelayCommand]
    private void ClearPacketLogs()
    {
        _allPacketLogs.Clear();
        FilteredPacketLogs.Clear();
    }

    private void ToggleLogger()
    {
        if (_isReceivingPackets)
            _flash.FlashCall += LogPackets;
        else
            _flash.FlashCall -= LogPackets;
    }

    private bool _filterEnabled
    {
        get
        {
            foreach (PacketLogFilterViewModel filter in _allPacketFilters)
            {
                if (!filter.IsChecked)
                    return true;
            }
            return false;
        }
    }

    private void LogPackets(string function, object[] args)
    {
        if (function != "packet")
            return;

        string packetText = args[0].ToString()!;

        if (!_filterEnabled)
        {
            AddPacket(packetText);
            return;
        }

        string[] packet = packetText.Split(new[] { '%' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (PacketLogFilterViewModel filterVM in _allPacketFilters)
        {
            if (!filterVM.IsChecked && filterVM.Filter.Invoke(packet))
                return;
        }

        AddPacket(packetText);
    }

    partial void OnPacketSearchTextChanged(string value)
    {
        ApplyPacketSearch();
    }

    private void AddPacket(string packetText)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _allPacketLogs.Add(packetText);
            if (_allPacketLogs.Count > 1000)
                _allPacketLogs.RemoveAt(0);

            if (MatchesPacketSearch(packetText))
            {
                FilteredPacketLogs.Add(packetText);
                if (FilteredPacketLogs.Count > 1000)
                    FilteredPacketLogs.RemoveAt(0);
            }
        });
    }

    private void ApplyPacketSearch()
    {
        IEnumerable<string> filtered = _allPacketLogs.Where(MatchesPacketSearch);
        FilteredPacketLogs = new ObservableCollection<string>(filtered);
    }

    private bool MatchesPacketSearch(string packet)
    {
        return string.IsNullOrWhiteSpace(PacketSearchText)
            || packet.Contains(PacketSearchText, StringComparison.OrdinalIgnoreCase);
    }
}
