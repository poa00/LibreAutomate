﻿using Au.Controls;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Au.Tools
{
	class KeysWindow : InfoWindow //KPopup
	{
		public KeysWindow() : base(0) {
			Size = (500, 240);
			WindowName = "Keys";
			Name = "Ci.Keys"; //prevent hiding when activated
			CloseHides = true;
		}

		protected override void OnHandleCreated() {
			var c = Control1;
			c.ZTags.AddLinkTag("+a", o => _Insert(o)); //link that inserts a key etc
			c.ZTags.SetLinkStyle(new SciTags.UserDefinedStyle { textColor = 0x0080FF, underline = false }); //remove underline from links

			var s = ResourceUtil.GetString("tools/keys.txt").RxReplace(@"\{(.+?)\}(?!\})", "<+a>$1<>");
			this.Text = s;

			base.OnHandleCreated();
		}

		void _Insert(string s) {
			Debug.Assert(InsertInControl == Panels.Editor.ZActiveDoc);
			if (!CodeInfo.GetDocumentAndFindNode(out var cd, out var node)) return;
			switch (node) {
			case LiteralExpressionSyntax when node.Kind() == SyntaxKind.StringLiteralExpression: break;
			case InterpolatedStringExpressionSyntax: break;
			default: if ((node = node.Parent) is InterpolatedStringExpressionSyntax) break; return;
			}
			var code = cd.code;
			var pos = cd.pos16;
			var sci = cd.sciDoc;
			var span = node.Span;
			int from = span.Start, to = span.End;
			while (code[from] is '@' or '$') from++; from++;
			if (to > from && code[to - 1] == '\"') to--;

			switch (s) {
			case "text": _AddArg(", \"!\b\""); return;
			case "html": _AddArg(", \"%\b\""); return;
			case "sleepMs": _AddArg(", 100"); return;
			case "keyCode": _AddArg(", KKey.Left"); return;
			case "scanCode": _AddArg(", new KKeyScan(1, false)"); return;
			case "action": _AddArg(", new Action(() => { mouse.rightClick(); })"); return;
			}

			void _AddArg(string s) {
				if (to == span.End) s = "\"" + s;
				sci.zGoToPos(true, span.End);
				InsertCode.TextSimplyInControl(sci, s);
			}

			bool addArg = code[from] is '!' or '%' || code[from..pos].Contains('^');

			if (s.Length == 2 && s[0] != '#' && !s[0].IsAsciiAlpha()) s = s[0] == '\\' ? "|" : s[..1]; //eg 2@ or /? or \|

			string prefix = null, suffix = null;
			char k1 = code[pos - 1], k2 = code[pos];
			if (!addArg) {
				if (s[0] is '*' or '+') {
					if (k1 is '*' or '+') sci.zSelect(true, pos - 1, pos); //eg remove + from Alt+ if now selected *down
				} else {
					if (pos > from && k1 > ' ' && k1 != '(' && !(k1 == '+' && !code.Eq(pos - 2, '#'))) prefix = " ";
				}
			}
			if (0 != s.Ends(false, "Alt", "Ctrl", "Shift", "Win")) suffix = "+";
			else if (!addArg && pos < to && k2 > ' ' && k2 is not (')' or '+' or '*')) suffix = "\b ";

			bool ok = true;
			if (s.Starts("right")) ok = _Menu("RAlt", "RCtrl", "RShift", "RWin");
			else if (s.Starts("lock")) ok = _Menu("CapsLock", "NumLock", "ScrollLock");
			else if (s.Starts("other")) ok = _Menu(s_rare);
			if (!ok) return;

			bool _Menu(params string[] a) {
				int j = popupMenu.showSimple(a) - 1;
				if (j < 0) return false;
				s = a[j];
				j = s.IndexOf(' '); if (j > 0) s = s[..j];
				return true;
			}

			s = prefix + s + suffix;

			if (addArg) {
				_AddArg($", \"{s}\b\"");
			} else {
				InsertCode.TextSimplyInControl(sci, s);
			}
		}

		static string[] s_rare = {
"BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch", "BrowserFavorites", "BrowserHome",
"LaunchMail", "LaunchMediaSelect", "LaunchApp1", "LaunchApp2",
"MediaNextTrack", "MediaPrevTrack", "MediaStop", "MediaPlayPause",
"VolumeMute", "VolumeDown", "VolumeUp",
"IMEKanaMode", "IMEHangulMode", "IMEJunjaMode", "IMEFinalMode", "IMEHanjaMode", "IMEKanjiMode", "IMEConvert", "IMENonconvert", "IMEAccept", "IMEModeChange", "IMEProcessKey",
"Break  //Ctrl+Pause", "Clear  //Shift+#5", "Sleep",
//"F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24", //rejected
  };
	}
}
