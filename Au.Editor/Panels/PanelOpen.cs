using Au.Controls;
using System.Windows.Controls;
using System.Windows.Input;

class PanelOpen {
	KTreeView _tv;
	bool _updatedOnce;
	bool _closing;

	public PanelOpen() {
		//P.UiaSetName("Open panel"); //no UIA element for Panel

		_tv = new KTreeView { Name = "Open_list" };
		P.Children.Add(_tv);
	}

	public DockPanel P { get; } = new();

	public void UpdateList() {
		//_tv.SetItems(App.Model.OpenFiles, _updatedOnce); //this would be ok, but displays yellow etc
		var a = App.Model.OpenFiles;
		_tv.SetItems(a.Select(o => new _Item { f = o }), _updatedOnce);
		if (a.Count > 0) {
			_tv.SetFocusedItem(0, _closing ? 0 : TVFocus.EnsureVisible);
			_tv.SelectSingle(0, andFocus: false);
		}
		if (!_updatedOnce) {
			_updatedOnce = true;
			FilesModel.NeedRedraw += v => { _tv.Redraw(v.remeasure); };
			_tv.ItemClick += _tv_ItemClick;
			//_tv.ContextMenuOpening += (_,_) => //never mind
		}
	}

	private void _tv_ItemClick(TVItemEventArgs e) {
		if (e.Mod != 0 || e.ClickCount != 1) return;
		var f = (e.Item as _Item).f;
		switch (e.Button) {
		case MouseButton.Left:
			App.Model.SetCurrentFile(f);
			break;
		case MouseButton.Right:
			_tv.SelectSingle(e.Item, andFocus: false);
			switch (popupMenu.showSimple("Close\tM-click|Close all other|Close all")) {
			case 0:
				_tv.SelectSingle(0, andFocus: false);
				break;
			case 1:
				_CloseFile();
				break;
			case 2:
				App.Model.CloseEtc(FilesModel.ECloseCmd.CloseAll, dontClose: f);
				break;
			case 3:
				App.Model.CloseEtc(FilesModel.ECloseCmd.CloseAll);
				break;
			}
			break;
		case MouseButton.Middle:
			_CloseFile();
			break;
		}

		void _CloseFile() {
			_closing = true; //prevent scrolling to top when closing an item near the bottom
			App.Model.CloseFile(f, selectOther: true);
			_closing = false;
		}
	}

	class _Item : ITreeViewItem {
		public FileNode f;

		#region ITreeViewItem

		string ITreeViewItem.DisplayText => f.DisplayName;

		object ITreeViewItem.Image => f.Image;

		#endregion
	}
}
