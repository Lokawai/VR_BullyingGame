using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.Runtime;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.LongTermMemory
{
    /// <summary>
    ///     Handles Long Term Memory (End User) management logic.
    /// </summary>
    public class LongTermMemoryLogic : IDisposable
    {
        private const string SelectAllText = "Select All";
        private const string UnselectAllText = "Unselect All";
        private readonly ConfigurationWindowContext _context;
        private readonly List<string> _selectedEndUserIds = new();
        private readonly ConvaiLongTermMemorySection _ui;
        private bool _isDeleting;

        private bool _isDisposed;
        private bool _isRefreshing;
        private bool _isSectionVisible;
        private int _requestVersion;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LongTermMemoryLogic" /> class.
        /// </summary>
        /// <param name="section">UI section instance to bind to.</param>
        /// <param name="context">Shared window context.</param>
        public LongTermMemoryLogic(ConvaiLongTermMemorySection section, ConfigurationWindowContext context)
        {
            _ui = section;
            _context = context;

            _ui.RefreshButton.clicked += RefreshEndUserList;
            _ui.RetryButton.clicked += RefreshEndUserList;
            _ui.SelectAllButton.clicked += SelectAllEndUsers;
            _ui.DeleteButton.clicked += DeleteSelectedEndUsers;

            if (_context != null) _context.ApiKeyAvailabilityChanged += OnApiKeyAvailabilityChanged;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            CancelPendingRequests();

            _ui.RefreshButton.clicked -= RefreshEndUserList;
            _ui.RetryButton.clicked -= RefreshEndUserList;
            _ui.SelectAllButton.clicked -= SelectAllEndUsers;
            _ui.DeleteButton.clicked -= DeleteSelectedEndUsers;

            if (_context != null) _context.ApiKeyAvailabilityChanged -= OnApiKeyAvailabilityChanged;
        }

        /// <summary>
        ///     Sets whether the section is currently visible.
        /// </summary>
        /// <param name="isVisible">True if section is visible.</param>
        public void SetSectionVisible(bool isVisible)
        {
            if (_isSectionVisible == isVisible) return;

            _isSectionVisible = isVisible;
            if (!_isSectionVisible)
            {
                CancelPendingRequests();
                return;
            }

            RefreshEndUserList();
        }

        private void OnApiKeyAvailabilityChanged(bool hasApiKey)
        {
            if (!_isSectionVisible) return;

            if (hasApiKey)
                RefreshEndUserList();
            else
                ShowInvalidApiKeyState();
        }

        private void CancelPendingRequests()
        {
            _requestVersion++;
            _isRefreshing = false;
            _isDeleting = false;
        }

        private void SelectAllEndUsers()
        {
            int itemCount = GetLtmItemCount();
            if (itemCount == 0) return;

            bool allSelected = _selectedEndUserIds.Count == itemCount;
            if (allSelected)
                UnselectAllEndUsers();
            else
            {
                _selectedEndUserIds.Clear();
                foreach (VisualElement child in _ui.IDContainer.Children())
                {
                    if (child is LTMItemUI item)
                    {
                        item.SetSelected(true);
                        _selectedEndUserIds.Add(item.EndUserId);
                    }
                }

                _ui.DeleteButton.SetEnabled(true);
                _ui.SelectAllButton.text = UnselectAllText;
            }
        }

        private void UnselectAllEndUsers()
        {
            _selectedEndUserIds.Clear();
            foreach (VisualElement child in _ui.IDContainer.Children())
            {
                if (child is LTMItemUI item)
                    item.SetSelected(false);
            }

            _ui.DeleteButton.SetEnabled(false);
            _ui.SelectAllButton.text = SelectAllText;
        }

        private int GetLtmItemCount()
        {
            int count = 0;
            foreach (VisualElement child in _ui.IDContainer.Children())
            {
                if (child is LTMItemUI)
                    count++;
            }

            return count;
        }

        private void RefreshSelectAllButtonText()
        {
            int itemCount = GetLtmItemCount();
            if (itemCount == 0)
            {
                _ui.SelectAllButton.text = SelectAllText;
                return;
            }

            _ui.SelectAllButton.text = _selectedEndUserIds.Count == itemCount ? UnselectAllText : SelectAllText;
        }

        private void DeleteSelectedEndUsers() => _ = DeleteSelectedEndUsersAsync(++_requestVersion);

        private async Task DeleteSelectedEndUsersAsync(int requestId)
        {
            if (_selectedEndUserIds.Count == 0 || _isDeleting || !_isSectionVisible) return;

            if (!EditorUtility.DisplayDialog(
                    "Delete Long Term Memory Users",
                    $"Delete {_selectedEndUserIds.Count} selected user(s)? This cannot be undone.",
                    "Delete",
                    "Cancel"))
                return;

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ShowInvalidApiKeyState();
                return;
            }

            _isDeleting = true;
            SetLoadingState("Deleting selected users...");
            _ui.DeleteButton.SetEnabled(false);

            try
            {
                var options = new ConvaiRestClientOptions(settings.ApiKey);
                using var client = new ConvaiRestClient(options);

                List<Task<bool>> deleteTasks = new();
                foreach (string endUserId in _selectedEndUserIds)
                    deleteTasks.Add(DeleteEndUserAsync(client, endUserId));

                bool[] results = await Task.WhenAll(deleteTasks);
                if (ShouldIgnoreResult(requestId)) return;

                bool allSuccessful = results.All(success => success);
                _isDeleting = false;
                SetStatus(
                    allSuccessful
                        ? "Selected users deleted successfully."
                        : "Some users could not be deleted. Refresh and retry.",
                    !allSuccessful);

                RefreshEndUserList();
            }
            finally
            {
                _isDeleting = false;
            }
        }

        private void RefreshEndUserList() => _ = RefreshEndUserListAsync(++_requestVersion);

        private async Task RefreshEndUserListAsync(int requestId)
        {
            if (_isRefreshing || !_isSectionVisible || _isDisposed) return;

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ShowInvalidApiKeyState();
                return;
            }

            _isRefreshing = true;
            SetLoadingState("Loading Long Term Memory users...");

            try
            {
                var options = new ConvaiRestClientOptions(settings.ApiKey);
                using var client = new ConvaiRestClient(options);
                EndUsersListResponse response = await client.Ltm.GetEndUsersAsync();

                if (ShouldIgnoreResult(requestId)) return;

                PopulateEndUserList(response);
                SetStatus("Long Term Memory users loaded.", false);
            }
            catch (Exception ex)
            {
                if (ShouldIgnoreResult(requestId)) return;

                ConvaiLogger.Error($"Failed to fetch end users: {ex.Message}", LogCategory.REST);
                PopulateEndUserList(EndUsersListResponse.Default());
                SetStatus("Failed to load users. Check API key and network, then retry.", true);
            }
            finally
            {
                if (requestId == _requestVersion) _isRefreshing = false;
            }
        }

        private static async Task<bool> DeleteEndUserAsync(ConvaiRestClient client, string endUserId)
        {
            try
            {
                await client.Ltm.DeleteEndUserAsync(endUserId);
                ConvaiLogger.Debug($"Deleted end user {endUserId}.", LogCategory.REST);
                return true;
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Failed to delete end user: {ex.Message}", LogCategory.REST);
                return false;
            }
        }

        private bool ShouldIgnoreResult(int requestId) =>
            _isDisposed || requestId != _requestVersion || !_isSectionVisible;

        private void ShowInvalidApiKeyState()
        {
            _ui.IDContainer.Clear();
            _selectedEndUserIds.Clear();
            _ui.TableTitle.text = "No Long Term Memory Users Found";
            _ui.IDContainer.style.display = DisplayStyle.None;
            _ui.SelectAllButton.SetEnabled(false);
            _ui.SelectAllButton.text = SelectAllText;
            _ui.DeleteButton.SetEnabled(false);
            SetStatus("API key missing or invalid. Configure it in Project Settings.", false);
        }

        private void PopulateEndUserList(EndUsersListResponse response)
        {
            _ui.IDContainer.Clear();
            _selectedEndUserIds.Clear();

            List<EndUserDetails> endUsers = response?.EndUsers ?? new List<EndUserDetails>();
            if (endUsers.Count == 0)
            {
                _ui.TableTitle.text = "No Long Term Memory Users Found";
                _ui.SelectAllButton.SetEnabled(false);
                _ui.SelectAllButton.text = SelectAllText;
                _ui.IDContainer.style.display = DisplayStyle.None;
                _ui.DeleteButton.SetEnabled(false);
                return;
            }

            _ui.TableTitle.text = $"Long Term Memory Users ({response.TotalCount} total)";
            _ui.IDContainer.style.display = DisplayStyle.Flex;
            _ui.SelectAllButton.SetEnabled(true);
            _ui.SelectAllButton.text = SelectAllText;
            _ui.DeleteButton.SetEnabled(false);

            foreach (EndUserDetails endUser in endUsers)
            {
                var item = new LTMItemUI(endUser.DisplayName, endUser.EndUserId, endUser.ShortId, OnEndUserSelected);
                _ui.IDContainer.Add(item);
            }
        }

        private void OnEndUserSelected(bool selected, string endUserId)
        {
            if (selected)
                _selectedEndUserIds.Add(endUserId);
            else
                _selectedEndUserIds.Remove(endUserId);

            _ui.DeleteButton.SetEnabled(_selectedEndUserIds.Count > 0);
            RefreshSelectAllButtonText();
        }

        private void SetLoadingState(string message)
        {
            SetStatus(message, false);
            _ui.RefreshButton.SetEnabled(false);
            _ui.RetryButton.SetEnabled(false);
        }

        private void SetStatus(string message, bool showRetry)
        {
            _ui.StatusLabel.text = message;
            _ui.RetryButton.style.display = showRetry ? DisplayStyle.Flex : DisplayStyle.None;
            _ui.RetryButton.SetEnabled(showRetry);
            _ui.RefreshButton.SetEnabled(!_isRefreshing && !_isDeleting);
        }
    }
}
