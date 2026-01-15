using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class GroupPostsViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;
    private readonly ICacheService _cacheService;

    [ObservableProperty] private ObservableCollection<GroupPostItem> _posts = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _canLoadMore = true;
    [ObservableProperty] private string _status = "Click Refresh to load posts";
    [ObservableProperty] private bool _showCreatePanel;
    [ObservableProperty] private bool _isBusy;
    
    // Create/Edit fields
    [ObservableProperty] private string _postTitle = string.Empty;
    [ObservableProperty] private string _postText = string.Empty;
    [ObservableProperty] private string _postVisibility = "group";
    [ObservableProperty] private bool _sendNotification = true;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private string? _editingPostId;
    
    private int _offset;
    private const int PageSize = 20;

    public ObservableCollection<string> VisibilityOptions { get; } = new(new[] { "group", "public" });

    public GroupPostsViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();
    }

    [RelayCommand]
    private async Task LoadPostsAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Please set a Group ID first";
            return;
        }

        IsLoading = true;
        Status = "Loading posts...";
        Posts.Clear();
        _offset = 0;

        try
        {
            var posts = await _apiService.GetGroupPostsAsync(groupId, PageSize, 0);
            foreach (var p in posts)
            {
                Posts.Add(new GroupPostItem(p));
            }
            _offset = posts.Count;
            CanLoadMore = posts.Count >= PageSize;
            await _cacheService.SaveAsync($"group_posts_{groupId}", Posts.ToList());
            Status = posts.Count == 0 ? "No posts found" : $"Loaded {posts.Count} posts";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore || IsLoading) return;

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        IsLoading = true;

        try
        {
            var posts = await _apiService.GetGroupPostsAsync(groupId, PageSize, _offset);
            foreach (var p in posts)
            {
                Posts.Add(new GroupPostItem(p));
            }
            _offset += posts.Count;
            CanLoadMore = posts.Count >= PageSize;
            await _cacheService.SaveAsync($"group_posts_{groupId}", Posts.ToList());
            Status = $"Loaded {Posts.Count} posts total";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadFromCacheAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Please set a Group ID first";
            return;
        }

        Status = "Loading cached posts...";
        var cached = await _cacheService.LoadAsync<List<GroupPostItem>>($"group_posts_{groupId}");
        if (cached == null || cached.Count == 0)
        {
            Status = "No cached posts found";
            return;
        }

        Posts.Clear();
        foreach (var item in cached)
        {
            Posts.Add(item);
        }

        _offset = Posts.Count;
        CanLoadMore = Posts.Count >= PageSize;
        Status = $"Loaded {Posts.Count} cached posts";
    }

    [RelayCommand]
    private void ToggleCreatePanel()
    {
        if (ShowCreatePanel && IsEditMode)
        {
            CancelEdit();
        }
        else
        {
            ShowCreatePanel = !ShowCreatePanel;
            if (ShowCreatePanel)
            {
                IsEditMode = false;
                EditingPostId = null;
                PostTitle = string.Empty;
                PostText = string.Empty;
                PostVisibility = "group";
                SendNotification = true;
            }
        }
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first";
            return;
        }

        if (string.IsNullOrWhiteSpace(PostTitle) || string.IsNullOrWhiteSpace(PostText))
        {
            Status = "Title and content are required";
            return;
        }

        IsBusy = true;
        Status = IsEditMode ? "Updating post..." : "Creating post...";

        try
        {
            if (IsEditMode && !string.IsNullOrEmpty(EditingPostId))
            {
                await _apiService.UpdateGroupPostAsync(groupId, EditingPostId, PostTitle, PostText, PostVisibility, SendNotification);
                Status = "Post updated!";
            }
            else
            {
                await _apiService.CreateGroupPostAsync(groupId, PostTitle, PostText, null, PostVisibility, SendNotification);
                Status = "Post created!";
            }

            ShowCreatePanel = false;
            IsEditMode = false;
            EditingPostId = null;
            PostTitle = string.Empty;
            PostText = string.Empty;

            await LoadPostsAsync();
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void EditPost(GroupPostItem post)
    {
        PostTitle = post.Title;
        PostText = post.Text;
        PostVisibility = post.Visibility;
        EditingPostId = post.Id;
        IsEditMode = true;
        ShowCreatePanel = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ShowCreatePanel = false;
        IsEditMode = false;
        EditingPostId = null;
        PostTitle = string.Empty;
        PostText = string.Empty;
    }

    [RelayCommand]
    private async Task DeletePostAsync(string postId)
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(postId)) return;

        IsBusy = true;
        Status = "Deleting post...";

        try
        {
            await _apiService.DeleteGroupPostAsync(groupId, postId);
            Status = "Post deleted!";
            await LoadPostsAsync();
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public class GroupPostItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string VisibilityIcon => Visibility == "public" ? "🌍" : "👥";

    public GroupPostItem() { }

    public GroupPostItem(GroupPost post)
    {
        Id = post.Id;
        Title = post.Title ?? "(No title)";
        Text = post.Text ?? "";
        Visibility = post.Visibility ?? "group";
        CreatedAt = post.CreatedAt.ToString("MMM dd, yyyy HH:mm");
    }
}
