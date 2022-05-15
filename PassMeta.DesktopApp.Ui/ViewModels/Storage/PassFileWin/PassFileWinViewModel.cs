namespace PassMeta.DesktopApp.Ui.ViewModels.Storage.PassFileWin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Media;
    using Common;
    using Common.Constants;
    using Common.Enums;
    using Common.Interfaces;
    using Common.Interfaces.Services;
    using Common.Interfaces.Services.PassFile;
    using Common.Models.Entities;
    using Common.Models.Entities.Extra;
    using Common.Utils.Extensions;
    using Components;
    using Constants;
    using Core;
    using Core.Utils;
    using Core.Utils.Extensions;
    using Models;
    using ReactiveUI;
    using Utils.Extensions;
    using Views.Main;
    using Views.Storage;

    public class PassFileWinViewModel : ReactiveObject
    {
        public bool PassFileChanged { get; private set; }

        private PassFile? _passFile;
        public PassFile? PassFile 
        { 
            get => _passFile;
            private set => this.RaiseAndSetIfChanged(ref _passFile, value);
        }
        
        public IObservable<string> Title { get; }
        
        public IObservable<bool> ReadOnly { get; }
        
        #region Input
        
        private string? _name;
        public string? Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }
        
        private int _selectedColorIndex;
        public int SelectedColorIndex
        {
            get => _selectedColorIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedColorIndex, value);
        }

        #endregion

        #region Read-only fields

        public IObservable<string?> CreatedOn { get; }
        public IObservable<string?> ChangedOn { get; }
        public IObservable<ISolidColorBrush> StateColor { get; }
        public IObservable<string> State { get; }

        #endregion
        
        #region Bottom buttons

        public BtnState OkBtn { get; }
        public BtnState ChangePasswordBtn { get; }
        public BtnState MergeBtn { get; }
        public BtnState ExportBtn { get; }
        public BtnState RestoreBtn { get; }
        public BtnState DeleteBtn { get; }
        
        #endregion
        
        public readonly ViewElements ViewElements = new();

        private readonly IDialogService _dialogService = EnvironmentContainer.Resolve<IDialogService>();
        private readonly IPassFileImportService _importService = EnvironmentContainer.Resolve<IPassFileImportService>();
        private readonly IPassFileExportService _exportService = EnvironmentContainer.Resolve<IPassFileExportService>();
        private readonly IPassFileMergeService _mergeService = EnvironmentContainer.Resolve<IPassFileMergeService>();

        public PassFileWinViewModel(PassFile passFile)
        {
            PassFile = passFile;
            PassFileChanged = false;

            _name = passFile.Name;
            SelectedColorIndex = PassFileColor.List.IndexOf(passFile.GetPassFileColor());

            var passFileChanged = this.WhenAnyValue(vm => vm.PassFile);

            var anyChanged = this.WhenAnyValue(
                    vm => vm.Name,
                    vm => vm.SelectedColorIndex,
                    vm => vm.PassFile)
                .Select(val =>
                    val.Item3 is not null && (
                        val.Item1 != val.Item3.Name ||
                        PassFileColor.List[val.Item2] != val.Item3.GetPassFileColor()));

            Title = passFileChanged.Select(pf => pf is null
                ? string.Empty
                : string.Format(pf.LocalCreated
                    ? Resources.PASSFILE__TITLE_NEW
                    : pf.LocalDeleted
                        ? Resources.PASSFILE__TITLE_DELETED
                        : Resources.PASSFILE__TITLE, pf.GetIdentityString()));

            CreatedOn = passFileChanged.Select(pf => pf?.CreatedOn.ToShortDateTimeString());

            ChangedOn = passFileChanged.Select(pf => string.Join(" / ", 
                new[] {pf?.InfoChangedOn, pf?.VersionChangedOn}
                    .Where(dt => dt is not null)
                    .Select(dt => dt!.Value.ToShortDateTimeString())));

            StateColor = passFileChanged.Select(pf => pf.GetStateColor());

            State = passFileChanged.Select(pf => pf is null ? string.Empty : _MakeState(pf));

            OkBtn = new BtnState
            {
                ContentObservable = anyChanged.Select(changed => changed
                    ? Resources.PASSFILE__BTN_SAVE
                    : Resources.PASSFILE__BTN_OK),
                CommandObservable = anyChanged.Select(changed => ReactiveCommand.Create(changed ? Save : Close))
            };

            ChangePasswordBtn = new BtnState
            {
                CommandObservable = Observable.Return(ReactiveCommand.CreateFromTask(ChangePasswordAsync)),
                IsVisibleObservable = passFileChanged.Select(pf => pf?.LocalDeleted is false)
            };

            MergeBtn = new BtnState
            {
                CommandObservable = Observable.Return(ReactiveCommand.CreateFromTask(MergeAsync)),
                IsVisibleObservable = passFileChanged.Select(pf => pf?.LocalDeleted is false
                                                                   && pf.Problem?.Kind is PassFileProblemKind.NeedsMerge)
            };
            
            ExportBtn = new BtnState
            {
                CommandObservable = Observable.Return(ReactiveCommand.CreateFromTask(ExportAsync)),
                IsVisibleObservable = passFileChanged.Select(pf => pf?.LocalDeleted is false)
            };

            RestoreBtn = new BtnState
            {
                CommandObservable = Observable.Return(ReactiveCommand.CreateFromTask(RestoreAsync)),
                IsVisibleObservable = passFileChanged.Select(pf => pf is not null)
            };

            DeleteBtn = new BtnState
            {
                CommandObservable = Observable.Return(ReactiveCommand.Create(Delete)),
                IsVisibleObservable = passFileChanged.Select(pf => pf?.LocalDeleted is false)
            };

            ReadOnly = passFileChanged.Select(pf => pf?.LocalDeleted is not false);
        }
        
        private static string _MakeState(PassFile passFile)
        {
            var states = new Stack<string>();

            states.Push(string.Format(
                passFile.LocalCreated
                    ? Resources.PASSFILE__STATE_LOCAL_CREATED
                    : passFile.LocalChanged
                        ? Resources.PASSFILE__STATE_LOCAL_CHANGED
                        : passFile.LocalDeleted
                            ? Resources.PASSFILE__STATE_LOCAL_DELETED
                            : passFile.Problem is null
                                ? Resources.PASSFILE__STATE_OK
                                : string.Empty, passFile.GetPassFileChangePeriod()));
            
            if (passFile.Problem is not null)
                states.Push(passFile.Problem.ToString());

            return string.Join(Environment.NewLine, states.Where(s => s != string.Empty));
        }

        public void Close() => ViewElements.Window!.Close();

        public async Task ChangePasswordAsync()
        {
            if (PassFile?.LocalDeleted is not false) return;

            var result = await PassFile.LoadIfRequiredAndDecryptAsync(_dialogService, true);
            if (result.Bad) return;

            var passPhraseNew = await _dialogService.AskPasswordAsync(Resources.PASSFILE__ASK_PASSPHRASE_NEW);
            if (passPhraseNew.Bad || passPhraseNew.Data == string.Empty) return;

            var passfile = PassFile.Copy();
            passfile.PassPhrase = passPhraseNew.Data;
            var updateResult = PassFileManager.UpdateData(passfile);

            if (updateResult.Ok)
            {
                PassFile = passfile;
                PassFileChanged = true;
                
                _dialogService.ShowInfo(Resources.PASSFILE__INFO_PASSPHRASE_CHANGED);
            }
            else
            {
                _dialogService.ShowError(updateResult.Message!);
            }
        }
        
        private void Save()
        {
            if (PassFile is null)
            {
                Close();
                return;
            }

            if (PassFile.LocalDeleted)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                _dialogService.ShowFailure(Resources.PASSFILE__VALIDATION__INCORRECT_NAME);
                return;
            }

            var passFile = PassFile.Copy();
            passFile.Name = Name.Trim();
            passFile.Color = PassFileColor.List[SelectedColorIndex].Hex;

            var result = PassFileManager.UpdateInfo(passFile);
            if (result.Ok)
            {
                PassFile = passFile;
                PassFileChanged = true;
            }
            else
            {
                _dialogService.ShowError(result.Message!);
            }
        }

        private void Delete()
        {
            if (PassFile?.LocalDeleted is not false) return;

            PassFile = PassFileManager.Delete(PassFile);
            PassFileChanged = true;

            if (PassFile is null) Close();
        }

        private async Task ExportAsync()
        {
            if (PassFile?.LocalDeleted is not false) return;
            
            var fileDialog = new SaveFileDialog
            {
                InitialFileName = PassFile.Name + ExternalFormat.PassfileEncrypted.FullExtension,
                DefaultExtension = ExternalFormat.PassfileEncrypted.FullExtension,
                Filters = new List<FileDialogFilter>
                {
                    new()
                    {
                        Name = ExternalFormat.PassfileEncrypted.Name,
                        Extensions = new List<string> { ExternalFormat.PassfileEncrypted.PureExtension }
                    },
                    new()
                    {
                        Name = ExternalFormat.PassfileDecrypted.Name,
                        Extensions = new List<string> { ExternalFormat.PassfileDecrypted.PureExtension }
                    }
                }
            };
            
            var filePath = await fileDialog.ShowAsync(MainWindow.Current!);
            if (string.IsNullOrEmpty(filePath)) return;

            var result = await _exportService.ExportAsync(PassFile, filePath);
            if (result.Bad) return;

            _dialogService.ShowInfo(string.Format(Resources.PASSFILE__SUCCESS_EXPORT, PassFile.Name, filePath));
        }

        private async Task RestoreAsync()
        {
            var passFile = PassFile;
            if (passFile is null) return;

            var selectResult = await new PassFileRestoreWin(passFile).ShowDialog<IResult?>(ViewElements.Window);
            if (selectResult?.Ok is not true) return;

            if (passFile.LocalDeleted)
            {
                var restoreResult = PassFileManager.Restore(passFile);
                if (restoreResult.Bad)
                {
                    _dialogService.ShowError(restoreResult.Message!);
                    return;
                }

                PassFile = null;
                PassFile = passFile;
                PassFileChanged = true;
            }

            var pathResult = selectResult as IResult<string>;
            var pfResult = selectResult as IResult<PassFile>;

            if (pathResult is not null)
            {
                var importResult = await _importService.ImportAsync(pathResult.Data!, passFile.PassPhrase);
                if (importResult.Bad) return;
                
                passFile.Data = importResult.Data.Sections;
                passFile.PassPhrase = importResult.Data.PassPhrase;
            }
            else if (pfResult is not null)
            {
                passFile.DataEncrypted = pfResult.Data!.DataEncrypted;
                passFile.PassPhrase = null;
            }
            else return;

            var updateResult = PassFileManager.UpdateData(passFile, pfResult is not null);
            if (updateResult.Ok)
            {
                PassFile = null;
                PassFile = passFile;
                PassFileChanged = true;
                _dialogService.ShowInfo(pathResult is null 
                    ? string.Format(Resources.PASSFILE__SUCCESS_RESTORE_FROM_SERVER, PassFile.Name)
                    : string.Format(Resources.PASSFILE__SUCCESS_RESTORE_FROM_FILE, PassFile.Name, Path.GetFileName(pathResult.Data)));
            }
            else
            {
                _dialogService.ShowError(updateResult.Message!);
            }
        }
        
        private async Task MergeAsync()
        {
            if (PassFile?.LocalDeleted is not false) return;
            
            var mergeResult = await _mergeService.LoadAndPrepareMergeAsync(PassFile!);
            if (mergeResult.Bad) return;

            var merge = mergeResult.Data!;

            if (merge.Conflicts.Any())
            {
                var result = await new PassFileMergeWin(merge).ShowDialog<IResult?>(ViewElements.Window);
                if (result?.Ok is not true) return;
            }

            var passfile = PassFile.Copy();
            passfile.Data = merge.ResultSections;
            passfile.Marks |= PassFileMark.Merged;

            var updateResult = PassFileManager.UpdateData(passfile);
            if (updateResult.Ok)
            {
                passfile.Problem = null;
                PassFileManager.TryResetProblem(passfile.Id);

                PassFile = passfile;
                PassFileChanged = true;
                
                _dialogService.ShowInfo(Resources.PASSFILE__INFO_MERGED);
            }
            else
            {
                _dialogService.ShowError(updateResult.Message!);
            }
        }

#pragma warning disable 8618
        public PassFileWinViewModel() {}
#pragma warning restore 8618
    }
}