using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PassMeta.DesktopApp.Common;
using PassMeta.DesktopApp.Common.Abstractions;
using PassMeta.DesktopApp.Common.Abstractions.App;
using PassMeta.DesktopApp.Common.Abstractions.PassFileContext;
using PassMeta.DesktopApp.Common.Abstractions.Services;
using PassMeta.DesktopApp.Common.Abstractions.Services.PassFileServices;
using PassMeta.DesktopApp.Common.Abstractions.Utils.Helpers;
using PassMeta.DesktopApp.Common.Enums;
using PassMeta.DesktopApp.Common.Extensions;
using PassMeta.DesktopApp.Common.Models;
using PassMeta.DesktopApp.Common.Models.App;
using PassMeta.DesktopApp.Common.Models.Entities.PassFile;
using PassMeta.DesktopApp.Ui.Models.Providers;
using PassMeta.DesktopApp.Ui.Models.ViewModels.Base;
using PassMeta.DesktopApp.Ui.Models.ViewModels.Pages.Account;
using PassMeta.DesktopApp.Ui.Models.ViewModels.Pages.StoragePage.Components;
using PassMeta.DesktopApp.Ui.Models.ViewModels.Pages.StoragePage.Extra;
using ReactiveUI;
using Splat;

namespace PassMeta.DesktopApp.Ui.Models.ViewModels.Pages.StoragePage;

/// <summary>
/// Passfile storage page ViewModel.
/// </summary>
public class PwdStorageModel : PageViewModel
{
    private readonly IPassFileSyncService _pfSyncService = Locator.Current.Resolve<IPassFileSyncService>();
    private readonly IDialogService _dialogService = Locator.Current.Resolve<IDialogService>();
    private readonly IUserContext _userContext = Locator.Current.Resolve<IUserContextProvider>().Current;

    private readonly IPassFileContext<PwdPassFile> _pfContext =
        Locator.Current.Resolve<IPassFileContextProvider>().For<PwdPassFile>();

    private readonly IPassFileDecryptionHelper _pfDecryptionHelper =
        Locator.Current.Resolve<IPassFileDecryptionHelper>();

    private static bool _loaded;
    private static readonly PassFileItemPath LastItemPath = new();

    /// <inheritdoc />
    public override ContentControl[] RightBarButtons => new ContentControl[]
    {
        new Button
        {
            Content = "\uE74E",
            Command = ReactiveCommand.CreateFromTask(SaveAsync),
            [!Visual.IsVisibleProperty] = this.WhenAnyValue(vm => vm.SectionEdit)
                .Select(edit => edit is not null)
                .ToBinding(),
            [!InputElement.IsEnabledProperty] = Locator.Current.Resolve<IPassFileContextProvider>()
                .For<PwdPassFile>()
                .AnyChangedSource
                .Select(state => state)
                .ToBinding(),
            [ToolTip.TipProperty] = Resources.STORAGE__RIGHT_BAR_TOOLTIP__SAVE,
            [ToolTip.PlacementProperty] = PlacementMode.Left
        },
        new Button
        {
            Content = "\uE70F",
            Command = ReactiveCommand.Create(ItemsEdit),
            [!Visual.IsVisibleProperty] = this.WhenAnyValue(vm => vm.SectionList.SelectedIndex, vm => vm.SectionEdit)
                .Select(x => x is { Item1: >= 0, Item2: null })
                .ToBinding(),
            [ToolTip.TipProperty] = Resources.STORAGE__RIGHT_BAR_TOOLTIP__EDIT_ITEMS,
            [ToolTip.PlacementProperty] = PlacementMode.Left
        },
        new Button
        {
            Content = "\uE711",
            Command = ReactiveCommand.Create(ItemsDiscardChanges),
            [!Visual.IsVisibleProperty] = this.WhenAnyValue(vm => vm.SectionList.SelectedIndex, vm => vm.SectionEdit)
                .Select(x => x is { Item1: >= 0, Item2: not null })
                .ToBinding(),
            [ToolTip.TipProperty] = Resources.STORAGE__RIGHT_BAR_TOOLTIP__DISCARD_ITEMS,
            [ToolTip.PlacementProperty] = PlacementMode.Left
        },
        new Button
        {
            Content = "\uE8FB",
            Command = ReactiveCommand.Create(ItemsApplyChanges),
            [!Visual.IsVisibleProperty] = this.WhenAnyValue(vm => vm.SectionList.SelectedIndex, vm => vm.SectionEdit)
                .Select(x => x is { Item1: >= 0, Item2: not null })
                .ToBinding(),
            [ToolTip.TipProperty] = Resources.STORAGE__RIGHT_BAR_TOOLTIP__APPLY_ITEMS,
            [ToolTip.PlacementProperty] = PlacementMode.Left
        },
        new Button
        {
            Content = "\uE74D",
            Command = ReactiveCommand.Create(SectionDeleteAsync),
            [!Visual.IsVisibleProperty] = this.WhenAnyValue(vm => vm.SectionList.SelectedIndex, vm => vm.SectionEdit)
                .Select(x => x is { Item1: >= 0, Item2: null })
                .ToBinding(),
            [ToolTip.TipProperty] = Resources.STORAGE__RIGHT_BAR_TOOLTIP__DELETE_SECTION,
            [ToolTip.PlacementProperty] = PlacementMode.Left
        },
    };

    public PwdStorageModel(IScreen hostScreen, HostWindowProvider windowProvider) : base(hostScreen)
    {
        PassFileList = new PassFileListModel<PwdPassFile>(windowProvider);
        PassFileList
            .WhenAnyValue(x => x.SelectedIndex)
            .InvokeCommand(ReactiveCommand.CreateFromTask<int>(OnSelectPassFileAsync));

        SectionList = new PwdSectionListModel();
        SectionList
            .WhenAnyValue(x => x.SelectedIndex)
            .InvokeCommand(ReactiveCommand.Create<int>(OnSelectSection));

        LayoutState = this.WhenAnyValue(vm => vm.PassFileList.SelectedIndex,
                vm => vm.SectionList.SelectedIndex)
            .Select(x => x.Item1 < 0
                ? InitLayoutState
                : x.Item2 < 0
                    ? AfterPassFileSelectionLayoutState
                    : AfterSectionSelectionLayoutState);

        var lastItemPath = LastItemPath.Copy();

        PassFileBarExpander.IsOpenedObservable
            .Subscribe(isOpened =>
            {
                PassFileBarExpander.AutoExpanding = !(isOpened && PassFileList.GetSelectedPassFile() is not null);
            });

        this.WhenNavigatedToObservable()
            .InvokeCommand(ReactiveCommand.Create(() => LoadPassFilesAsync(lastItemPath)));
    }
    
    public PassFileListModel<PwdPassFile> PassFileList { get; }

    public PwdSectionListModel SectionList { get; }

    public PwdSectionReadModel? SectionRead { get; set; }

    public PwdSectionEditModel? SectionEdit { get; set; }

    public PassFileBarExpander PassFileBarExpander { get; } = new();

    public IObservable<LayoutState> LayoutState { get; }

    /// <inheritdoc />
    protected override async ValueTask<IResult> CanLeaveAsync()
    {
        return SectionEdit is not null
            ? await _dialogService.ConfirmAsync(Resources.STORAGE__CONFIRM_SECTION_RESET)
            : Result.Failure();
    }

    /// <inheritdoc />
    public override async ValueTask RefreshAsync()
    {
        if (_userContext.UserId is null)
        {
            await new AuthPageModel(HostScreen).TryNavigateAsync();
            return;
        }

        if (_pfContext.AnyChanged)
        {
            var confirm = await _dialogService.ConfirmAsync(Resources.STORAGE__CONFIRM_ROLLBACK);
            if (confirm.Bad) return;

            _pfContext.Rollback();
        }

        _loaded = false;
        await LoadPassFilesAsync(LastItemPath.Copy());
    }


    private async Task LoadPassFilesAsync(PassFileItemPath lastItemPath)
    {
        // using var preloader = Locator.Current.Resolve<AppLoading>().General.Begin();
        //
        // if (!_loaded)
        // {
        //     await _pfSyncService.SynchronizeAsync(_pfContext);
        //     _loaded = true;
        // }
        //
        // UpdatePassFileList();
        //
        // if (lastItemPath.PassFileId is not null)
        // {
        //     var index = _passFileList.FindIndex(btn => btn.PassFile!.Id == lastItemPath.PassFileId.Value);
        //     if (index >= 0)
        //     {
        //         if ((_passFileList[index].PassFile as PwdPassFile).Content.PassPhrase is not null)
        //         {
        //             PassFilesSelectedIndex = index;
        //             if (lastItemPath.PassFileSectionId is not null)
        //             {
        //                 SelectedData.SelectedSectionIndex =
        //                     SelectedData.SectionsList!.FindIndex(
        //                         btn => btn.Section.Id == lastItemPath.PassFileSectionId);
        //             }
        //         }
        //     }
        // }
        //
        // PassFileBarExpander.IsOpened = true;
    }

    private async Task OnSelectPassFileAsync(int _)
    {
        var passFile = PassFileList.GetSelectedPassFile();
        if (passFile is null || passFile.IsLocalDeleted())
        {
            PassFileList.RollbackSelectedPassFile();
            return;
        }

        if (passFile.Content.Decrypted is null)
        {
            var result = await _pfDecryptionHelper.ProvideDecryptedContentAsync(passFile, _pfContext);
            if (result.Bad)
            {
                PassFileList.RollbackSelectedPassFile();
                return;
            }
        }

        SectionList.RefreshList(passFile.Content.Decrypted!);
    }

    private void OnSelectSection(int _)
    {
        var section = SectionList.GetSelectedSection();
        if (section is null)
        {
            SectionRead = null;
            SectionEdit = null;
        }
        else if (section.Mark.HasFlag(PwdSectionMark.Created))
        {
            SectionRead = null;
            SectionEdit = PwdSectionEditModel.From(section);
        }
        else
        {
            SectionRead = new PwdSectionReadModel(section);
            SectionEdit = null;
        }

        PassFileBarExpander.TryExecuteAutoExpanding(section is null);

        if (section is null)
        {
            PassFileBarExpander.AutoExpanding = true;
        }
    }

    private async Task SaveAsync()
    {
        using (Locator.Current.Resolve<AppLoading>().General.Begin())
        {
            await _pfSyncService.SynchronizeAsync(_pfContext);
        }

        await LoadPassFilesAsync(LastItemPath.Copy());
    }
    
    #region Sections

    private async Task SectionDeleteAsync()
    {
        var passFile = PassFileList.GetSelectedPassFile()!;
        var section = SectionList.GetSelectedSection()!;
            
        var confirm = await _dialogService.ConfirmAsync(string.Format(Resources.STORAGE__CONFIRM_DELETE_SECTION, section.Name));
        if (confirm.Bad) return;

        using var preloader = Locator.Current.Resolve<AppLoading>().General.Begin();

        passFile.Content.Decrypted!.RemoveAll(s => s.Id == section.Id);

        var result = _pfContext.UpdateContent(passFile);
        if (result.Ok)
        {
            SectionList.Remove(section);
        }
    }
        
    #endregion
    
    #region Items

    private void ItemsEdit() => 
        SectionEdit = PwdSectionEditModel.From(SectionList.GetSelectedSection()!);

    private void ItemsApplyChanges()
    {
        using var preloader = Locator.Current.Resolve<AppLoading>().General.Begin();

        var passFile = PassFileList.GetSelectedPassFile()!;
        var section = SectionList.GetSelectedSection()!;

        var sectionActual = SectionEdit!.ToSection();

        if (!section.Mark.HasFlag(PwdSectionMark.Created) && !sectionActual.DiffersFrom(section))
        {
            SectionEdit = null;
            SectionRead = new PwdSectionReadModel(sectionActual);
            return;
        }

        passFile.Content.Decrypted!.RemoveAll(x => x.Id == sectionActual.Id);
        passFile.Content.Decrypted.Add(sectionActual);

        var result = _pfContext.UpdateContent(passFile);
        if (result.Ok)
        {
            using (PassFileBarExpander.DisableAutoExpandingScoped())
            {
                SectionList.Refresh(sectionActual);
            }

            SectionEdit = null;
            SectionRead = new PwdSectionReadModel(sectionActual);
        }
    }

    private void ItemsDiscardChanges()
    {
        using (PassFileBarExpander.DisableAutoExpandingScoped())
        {
            var section = SectionList.GetSelectedSection()!;

            SectionEdit = null;

            if (section.Mark.HasFlag(PwdSectionMark.Created))
            {
                SectionList.Remove(section);
            }
            else
            {
                SectionRead = new PwdSectionReadModel(section);
            }
        }
    }

    #endregion

    #region Layout states

    private static readonly LayoutState InitLayoutState = new()
    {
        PassFilesPaneWidth = 250d,
        SectionsListMargin = new Thickness(0, 6, 5, 6)
    };

    private static readonly LayoutState AfterPassFileSelectionLayoutState = new()
    {
        PassFilesPaneWidth = 250d,
        SectionsListMargin = new Thickness(0, 6, -100, 6)
    };

    private static readonly LayoutState AfterSectionSelectionLayoutState = new()
    {
        PassFilesPaneWidth = 200d,
        SectionsListMargin = new Thickness(0, 6, 5, 6)
    };

    #endregion
}