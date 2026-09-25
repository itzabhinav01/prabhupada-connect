using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.UI.ViewModels
{
    public partial class RecentlyReadViewModel : ObservableObject
    {
        private readonly ICorpusRepository _corpusRepository;
        private readonly IUserRepository _userRepository;

        // Bounded display list - independent of the 500-row retention cap in
        // user.db (see MILESTONE_5_HISTORY_ARCHITECTURE.md section 4). The page
        // only ever needs to show a small recent slice.
        private const int DisplayLimit = 100;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusText = "Loading history...";

        [ObservableProperty]
        private RecentlyReadItem? _continueReadingItem;

        public ObservableCollection<RecentlyReadItem> Items { get; } = new();

        public RecentlyReadViewModel(ICorpusRepository corpusRepository, IUserRepository userRepository)
        {
            _corpusRepository = corpusRepository;
            _userRepository = userRepository;
        }

        public async Task LoadAsync()
        {
            IsLoading = true;
            Items.Clear();
            ContinueReadingItem = null;

            try
            {
                var history = await _userRepository.GetRecentHistoryAsync(DisplayLimit);
                if (history.Count == 0)
                {
                    StatusText = "Nothing here yet.";
                    return;
                }

                // One batched corpus query instead of one query per row.
                var keys = history.Select(h => h.RecordKey).ToList();
                var records = await _corpusRepository.GetRecordsAsync(keys);
                var recordsByKey = records.ToDictionary(r => r.RecordKey, r => r);

                foreach (var h in history)
                {
                    RecentlyReadItem item;
                    if (recordsByKey.TryGetValue(h.RecordKey, out var record))
                    {
                        item = new RecentlyReadItem
                        {
                            RecordKey = h.RecordKey,
                            LastOpenedUtc = h.LastOpenedUtc,
                            OpenCount = h.OpenCount,
                            IsAvailable = true,
                            BookKey = record.BookKey,
                            BookTitle = _corpusRepository.GetBookTitle(record.BookKey),
                            Reference = string.IsNullOrWhiteSpace(record.Reference) ? record.RecordKey : record.Reference!,
                            Title = record.Title
                        };
                    }
                    else
                    {
                        // Missing-RecordKey safety: never fabricate, never drop the
                        // underlying history row - just render it as unavailable.
                        item = new RecentlyReadItem
                        {
                            RecordKey = h.RecordKey,
                            LastOpenedUtc = h.LastOpenedUtc,
                            OpenCount = h.OpenCount,
                            IsAvailable = false,
                            BookTitle = "Record unavailable",
                            Reference = h.RecordKey
                        };
                    }
                    Items.Add(item);
                }

                ContinueReadingItem = Items.FirstOrDefault(i => i.IsAvailable);
                StatusText = $"{Items.Count} recently read";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecentlyRead] Failed to load reading history: {ex}");
                Items.Clear();
                ContinueReadingItem = null;
                StatusText = "Couldn't load your reading history. Please try again.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task ClearHistoryAsync()
        {
            try
            {
                await _userRepository.ClearHistoryAsync();
                Items.Clear();
                ContinueReadingItem = null;
                StatusText = "Nothing here yet.";
            }
            catch (Exception ex)
            {
                // Deliberately does NOT clear Items - if the delete didn't
                // actually succeed, the UI should keep showing what's really in
                // user.db rather than implying history was cleared when it wasn't.
                System.Diagnostics.Debug.WriteLine($"[RecentlyRead] Failed to clear history: {ex}");
                StatusText = "Couldn't clear your history. Please try again.";
            }
        }
    }
}
