using System.Linq;
using Au.Controls;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.CSharp.Extensions.ContextQuery;

class CiErrors
{
	SemanticModel _semo;
	List<(Diagnostic d, int start, int end)> _codeDiag;
	readonly List<(int from, int to, string s)> _metaErrors = new();
	readonly List<(int from, int to, string s)> _stringErrors = new();

	public void Indicators(int start16, int end16, bool pasting = false, bool pastingSilent = false) {
		_semo = null;

		if (!CodeInfo.GetContextAndDocument(out var cd, 0, metaToo: true)) return;
		var doc = cd.sciDoc;
		var code = cd.code;
		if (code.Eq(end16 - 1, '\n')) end16--; //don't include error from next line
		if (end16 <= start16) return;
		bool has = false;
		var semo = cd.document.GetSemanticModelAsync().Result;
		var a = semo.GetDiagnostics(TextSpan.FromBounds(start16, end16));
		if (!a.IsDefaultOrEmpty) {
			_codeDiag = new(a.Length);
			foreach (var d_ in a) {
				var d = d_;
				if (d.IsSuppressed) continue;
				var loc = d.Location; if (!loc.IsInSource) continue;
				var span = loc.SourceSpan;
				//print.it(d.Severity, span, d.Id);
				int start = span.Start, end = span.End;
				if (end == start) {
					if (end < code.Length && !(code[end] == '\r' || code[end] == '\n')) end++;
					else if (start > 0) start--;
				}
				var ec = (ErrorCode)d.Code;
				if (start == 0 && ec == ErrorCode.WRN_UnprocessedXMLComment) continue; //XML comment at start

				//workaround for: when starting to type the last top-level statement, if it's a word (a C# type or non-keyword), error
				//	"A namespace cannot directly contain members such as fields or methods". Users would think it's a bug of this program.
				//	Another workaround would be to add ';' after. But it does not work if it's a C# type eg 'int'.
				//	Let's ignore it in editor. It's a lesser evil. And nobody will compile such unfinished code.
				//if (ec == ErrorCode.ERR_NamespaceUnexpected) continue;
				if (ec == ErrorCode.ERR_NamespaceUnexpected) {
					//If unknown name, convert to ERR_NameNotInContext. Then on mouse hover will display tooltip with links to add using directive etc.
					var d2 = cd.document.WithText(SourceText.From(code.Insert(end, ";")));
					var m2 = d2.GetSemanticModelAsync().Result;
					//print.it(m2.GetDiagnostics(span));
					d = m2.GetDiagnostics(span).FirstOrDefault(o => (ErrorCode)o.Code == ErrorCode.ERR_NameNotInContext);
					if (d == null) continue;
					ec = ErrorCode.ERR_NameNotInContext;
				}

				if (!has) doc.InicatorsDiag_(has = true);
				var indic = d.Severity switch { DiagnosticSeverity.Error => SciCode.c_indicError, DiagnosticSeverity.Warning => SciCode.c_indicWarning, DiagnosticSeverity.Info => SciCode.c_indicInfo, _ => SciCode.c_indicDiagHidden };
				doc.zIndicatorAdd(true, indic, start..end);
				_codeDiag.Add((d, start, end));

				if (d.Severity == DiagnosticSeverity.Error) {
					switch (ec) {
					case ErrorCode.ERR_NameNotInContext
					or ErrorCode.ERR_SingleTypeNameNotFound
					or ErrorCode.ERR_NoSuchMemberOrExtension
					or ErrorCode.ERR_NoSuchMemberOrExtensionNeedUsing
					or ErrorCode.ERR_UnimplementedInterfaceMember
					or ErrorCode.ERR_UnimplementedAbstractMethod:
						_semo = semo;
						break;
					}
				}
			}
		}
		if (_metaErrors.Count > 0) {
			foreach (var v in _metaErrors) {
				if (v.to <= start16 || v.from >= end16) continue;
				if (!has) doc.InicatorsDiag_(has = true);
				doc.zIndicatorAdd(true, SciCode.c_indicError, v.from..v.to);
			}
		}
		_Strings(semo, cd, start16, end16);
		if (_stringErrors.Count > 0) {
			if (!has) doc.InicatorsDiag_(has = true);
			foreach (var v in _stringErrors) {
				doc.zIndicatorAdd(true, SciCode.c_indicWarning, v.from..v.to);
			}
		}
		if (!has) {
			doc.InicatorsDiag_(false);
			_codeDiag = null;
		} else if (pasting && _semo != null) {
			//insert missing using directives
			List<_MissingUsingError> amu = null;
			foreach (var v in _codeDiag) {
				if (!_MissingUsingError.IsMissingUsingError((ErrorCode)v.d.Code, out bool extMethod)) continue;
				var mu = new _MissingUsingError(code, v.start, v.end, extMethod, _semo);
				_MissingUsingError.AddToList(ref amu, mu);
			}
			//var e1 = _codeDiag.Where(o => o.d.Severity == DiagnosticSeverity.Error); print.it(e1.Count(), e1);
			//print.it(amu.Lenn_(), amu);

			List<string> usings = _GetMissingUsings(amu);
			if (usings != null) doc.Dispatcher.InvokeAsync(() => { //this func is called from scintilla notification
				if (!pastingSilent && !dialog.showYesNo("Add missing using directives?", string.Join('\n', usings), owner: doc)) return;
				InsertCode.UsingDirective(string.Join(';', usings), true);
			});
		}
	}

	void _Strings(SemanticModel semo, CodeInfo.Context cd, int start16, int end16) {
		//using var p1 = perf.local();
		_stringErrors.Clear();
		var code = cd.code;
		foreach (var node in semo.Root.DescendantNodes(TextSpan.FromBounds(start16, end16))) {
			var format = CiUtil.GetParameterStringFormat(node, semo, false);
			if (format == PSFormat.None || format == PSFormat.regexpReplacement) continue;
			var s = node.GetFirstToken().ValueText; //replaced escape sequences
			if (s.Length == 0) continue;
			string es = null;
			try {
				switch (format) {
				case PSFormat.NetRegex:
					new System.Text.RegularExpressions.Regex(s); //never mind: may have 'options' argument, eg ECMAScript or Compiled
					break;
				case PSFormat.regexp:
					new regexp(s);
					break;
				case PSFormat.wildex:
					if (s.Starts("***")) s = s[(s.IndexOf(' ') + 1)..]; //eg wnd.Child("***elmName ...")
					new wildex(s);
					break;
				case PSFormat.keys when s[0] is not ('!' or '%'):
					//TODO: warning if ")"
					new keys(null)
						.AddKey(KKey.A) //to avoid warning when eg "*5"
						.AddKeys(s);
					break;
				}
			}
			catch (ArgumentException ex) { es = ex.Message; }
			if (es != null) {
				var span = node.Span;
				_stringErrors.Add((span.Start, span.End, es));
			}
		}
	}

	public void ClearMetaErrors() => _metaErrors.Clear();

	public void AddMetaError(int from, int to, string s) => _metaErrors.Add((from, to > from ? to : from + 1, s));

	public void EraseIndicatorsInLine(SciCode doc, int pos8) {
		var (_, start, end) = doc.zLineStartEndFromPos(false, pos8, withRN: true);
		doc.zIndicatorClear(false, SciCode.c_indicDiagHidden, start..end);
		doc.zIndicatorClear(false, SciCode.c_indicInfo, start..end);
		doc.zIndicatorClear(false, SciCode.c_indicWarning, start..end);
		doc.zIndicatorClear(false, SciCode.c_indicError, start..end);
	}

	public void SciModified(SciCode doc, in Sci.SCNotification n) {
		//clear arrays to prevent showing tooltip because positions changed. But don't clear indicators because we'll update them soon.
		_codeDiag = null;
		_stringErrors.Clear();

		if (_pasting.doc != null && n.modificationType.Has(Sci.MOD.SC_MOD_INSERTTEXT | Sci.MOD.SC_PERFORMED_USER)) {
			var p = _pasting; _pasting = default;
			if (doc == p.doc && n.length > 3) {
				int start = doc.zPos16(n.position), end = doc.zPos16(n.position + n.length);
				Indicators(start, end, pasting: true, pastingSilent: p.silent);
			}
		}
	}
	(SciCode doc, bool silent) _pasting;

	public void Pasting(SciCode doc, bool silent) { _pasting = (doc, silent); }

	public System.Windows.Documents.Section GetPopupTextAt(SciCode doc, int pos8, int pos16, out Action<CiPopupText, string> onLinkClick) {
		onLinkClick = null;
		if (_codeDiag == null && _metaErrors.Count == 0 && _stringErrors.Count == 0) return null;
		if (pos8 < 0) return null;
		int all = doc.Call(Sci.SCI_INDICATORALLONFOR, pos8);
		//print.it(all);
		if (0 == (all & ((1 << SciCode.c_indicError) | (1 << SciCode.c_indicWarning) | (1 << SciCode.c_indicInfo) | (1 << SciCode.c_indicDiagHidden)))) return null;

		var x = new CiText();
		x.StartParagraph();

		ErrorCode ecPrev = 0;
		int implPos = -1; bool implInterface = false;
		for (int i = 0, n = _codeDiag?.Count ?? 0; i < n; i++) {
			var v = _codeDiag[i];
			if (pos16 < v.start || pos16 > v.end) continue;
			var d = v.d;
			var s1 = d.Severity switch { DiagnosticSeverity.Error => "Error", DiagnosticSeverity.Warning => "Warning", _ => "Info" };
			x.LineBreak(s1, notIfFirstInParagraph: true);
			x.Append(": " + d.GetMessage());

			var ec = (ErrorCode)d.Code;
			if (d.Severity == DiagnosticSeverity.Error) {
				if (_semo == null) continue;
				//print.it(ec, d.Id);
				if (_MissingUsingError.IsMissingUsingError(ec, out bool extMethod)) {
					if (ec == ecPrev && !extMethod) continue; //probably "not found 'AbcAttribute'" followed by "not found 'Abc'"
					ecPrev = ec;
					_UsingsEtc(x, v, doc, extMethod);
				} else {
					switch (ec) {
					case ErrorCode.ERR_DottedTypeNameNotFoundInNS: //using Namespace.NotFound;
						ecPrev = ec;
						x.Hyperlink("^r", "\nAdd assembly reference or class file...");
						break;
					case ErrorCode.ERR_UnimplementedInterfaceMember or ErrorCode.ERR_UnimplementedAbstractMethod:
						Debug.Assert(implPos == -1 || implPos == v.start);
						implPos = v.start;
						implInterface = ec == ErrorCode.ERR_UnimplementedInterfaceMember;
						break;
					}
				}
			} else if (d.Severity == DiagnosticSeverity.Warning) {
				switch (ec) {
				case ErrorCode.WRN_MissingXMLComment:
					_XmlComment(x/*, v*/);
					break;
				}
			}
		}
		if (implPos >= 0) _Implement(x, implPos, implInterface);

		_Also(_metaErrors, "Error: ");
		_Also(_stringErrors, null);
		void _Also(List<(int from, int to, string s)> a, string prefix) {
			foreach (var v in a) {
				if (pos16 < v.from || pos16 > v.to) continue;
				x.LineBreak(prefix, notIfFirstInParagraph: true);
				x.Append(v.s);
			}
		}

		x.EndParagraph();
		onLinkClick = (ph, e) => _LinkClicked(e);
		return x.Result;
	}

	record _MissingUsingError
	{
		public static bool IsMissingUsingError(ErrorCode ec, out bool extMethod) {
			extMethod = false;
			return ec switch {
				ErrorCode.ERR_NoSuchMemberOrExtension or ErrorCode.ERR_NoSuchMemberOrExtensionNeedUsing => extMethod = true, //these end with (are you missing a using directive...
				ErrorCode.ERR_NameNotInContext or ErrorCode.ERR_SingleTypeNameNotFound => true,
				_ => false,
			};
			//not tested: ERR_GlobalSingleTypeNameNotFound, ERR_DottedTypeNameNotFoundInAgg, ERR_AliasNotFound, ERR_TypeNotFound
		}

		public static void AddToList(ref List<_MissingUsingError> a, _MissingUsingError mu) {
			if (mu.isEM && mu.emReceiverType == null) return; //unlikely
			a ??= new();
			foreach (var v in a) if (v.name == mu.name && v.emReceiverType == mu.emReceiverType && v.isGeneric == mu.isGeneric && v.isAttribute == mu.isAttribute) return;
			a.Add(mu);
		}

		public _MissingUsingError(string code, int start, int end, bool extMethod, SemanticModel semo) {
			int end2 = code.IndexOf('<', start, end - start);
			if (end2 < 0) end2 = end; else isGeneric = true;
			name = code[start..end2];
			isAttribute = !extMethod && _IsAttributeNameWithoutSuffix(name, start, semo);
			if (isEM = extMethod) emReceiverType = _GetExtensionMethodReceiverType(semo, start);
			Debug_.PrintIf(extMethod && emReceiverType == null, "failed to get extension method receiver type"); //unlikely
		}

		public readonly string name;
		public readonly bool isEM, isGeneric, isAttribute;
		public readonly ITypeSymbol emReceiverType;
	}

	List<string> _GetMissingUsings(List<_MissingUsingError> a) {
		if (a.NE_()) return null;
		List<string> usings = null;
		var compilation = _semo.Compilation;
		var stack = new List<string>();
		int need = 0; foreach (var v in a) if (v.isEM) need |= 2; else need |= 1;
		//var p1 = perf.local();
		//var p1 = new perf.Instance { Incremental = true };
		_EnumNamespace(compilation.GlobalNamespace);
		//p1.Write();
		//p1.NW();

		//CONSIDER: async, because slow. Or use AssemblyMetadata.CachedSymbols (internal), it's faster except first time.
		void _EnumNamespace(INamespaceSymbol ns) {
			bool found = false;
			foreach (var nt in ns.GetMembers()) {
				string sn = nt.Name;
				//print.it("<>" + new string(' ', stack.Count) + (nt is INamespaceSymbol ? "<c blue>" + nt.ToString() + "<>" : nt.ToString())/*, nt.ContainingAssembly?.Name*/);
				if (sn.NE() || sn[0] == '<') continue;
				if (nt is INamespaceSymbol ins) {
					stack.Add(sn);
					_EnumNamespace(ins);
					stack.RemoveAt(stack.Count - 1);
				} else if (!found) { //else continue to search in nested namespaces
					var its = nt as INamedTypeSymbol;
					if (0 != (need & 1)) {
						foreach (var v in a) {
							if (v.isEM) continue;
							if (sn != v.name) {
								if (!(v.isAttribute && sn.Length == v.name.Length + 9 && sn.Starts(v.name) && sn.Ends("Attribute"))) continue;
							}
							if (v.isGeneric && !its.IsGenericType) continue;
							if (_AddNamespace(nt)) goto gNext;
						}
					}
					if (0 != (need & 2)) {
						//p1.First();
						if (its.IsStatic && its.MightContainExtensionMethods) { //fast, but without IsStatic slow first time
							foreach (var m in nt.GetMembers().OfType<IMethodSymbol>()) { //fast; slightly slower than nt.MemberNames.Contains(errName) which gets member types etc too
								foreach (var v in a) {
									if (!v.isEM) continue;
									if (m.Name == v.name && m.IsExtensionMethod) {
										if (null == m.ReduceExtensionMethod(v.emReceiverType)) { /*Debug_.Print(emReceiverType);*/ continue; }
										if (_AddNamespace(m)) goto gNext;
									}
								}
							}
						}
						//p1.Next();
					}
					gNext:;

					bool _AddNamespace(ISymbol sym) {
						if (!sym.IsAccessibleWithin(compilation.Assembly)) return false;
						(usings ??= new()).Add(string.Join('.', stack));
						return found = true;
					}
				}
			}
		}
		return usings;
	}

	void _UsingsEtc(CiText x, in (Diagnostic d, int start, int end) v, SciCode doc, bool extMethod) {
		var mu = new _MissingUsingError(doc.zText, v.start, v.end, extMethod, _semo);
		List<_MissingUsingError> amu = null;
		_MissingUsingError.AddToList(ref amu, mu);
		List<string> usings = _GetMissingUsings(amu);
		if (usings != null) {
			var sstart = doc.zPos8(v.start).ToString();
			x.Append("\nAdd using ");
			for (int i = 0; i < usings.Count; i++) {
				var u = usings[i];
				if (i > 0) x.Append(" or ");
				x.Hyperlink("^u" + sstart + u, u);
			}
			if (!extMethod) {
				x.Append("\nOr prefix ");
				for (int i = 0; i < usings.Count; i++) {
					var u = usings[i];
					if (i > 0) x.Append(" or ");
					x.Hyperlink("^p" + sstart + u, u);
				}
			}
		} else {
			x.Hyperlink("^r", "\nAdd assembly reference or class file...");
			if (!(mu.isEM | mu.isGeneric | mu.isAttribute)) x.Hyperlink("^w" + mu.name, "\nFind Windows API...");
		}
	}

	void _LinkClicked(string s) {
		CodeInfo.HideTextPopup();
		char action = s[1];
		if (action == 'u' || action == 'p') { //add 'using', prefix namespace
			int pos8 = s.ToInt(2, out int i);
			s = s[i..];
			var doc = Panels.Editor.ZActiveDoc;
			EraseIndicatorsInLine(doc, pos8);
			if (action == 'p') {
				doc.zInsertText(false, pos8, s + ".", addUndoPointAfter: true);
			} else {
				InsertCode.UsingDirective(s, true);
			}
		} else if (action == 'w') { //Windows API
			new Au.Tools.DWinapi(s[2..]).Show();
		} else if (action == 'r') { //Add reference
			Menus.File.Properties();
		} else if (action == 'i') { //implement interface or abstract class
			InsertCode.ImplementInterfaceOrAbstractClass(s[2] == 'e', s.ToInt(3));
		}
	}

	static void _Implement(CiText x, int pos, bool isInterface) {
		x.Hyperlink("^ii" + pos, "\nImplement " + (isInterface ? "interface" : "abstract class"));
		if (isInterface) x.Hyperlink("^ie" + pos, "\nImplement explicitly");
	}

	static void _XmlComment(CiText x/*, in (Diagnostic d, int start, int end) v*/) {
		//x.Hyperlink("^xa+v.start, "\nAdd XML comment");
		//x.Hyperlink("^xd+v.start, "\nDisable warning");

		x.Append("\nTo add XML comment, type /// above.");
		x.Append("\nTo disable warning, add just /// or disable warning 1591 (use warningDisableSnippet).");
	}

	static bool _IsAttributeNameWithoutSuffix(string name, int pos, SemanticModel semo) {
		if (name.Ends("Attribute")) return false;
		return semo.SyntaxTree.IsAttributeNameContext(pos, default);
	}

	static ITypeSymbol _GetExtensionMethodReceiverType(SemanticModel semo, int startOfMethodName) {
		ITypeSymbol t = null;
		if (semo.SyntaxTree.GetRoot().FindToken(startOfMethodName).Parent.Parent is MemberAccessExpressionSyntax ma)
			t = semo.GetTypeInfo(ma.Expression).Type;
		Debug_.PrintIf(t == null, "failed to get extension method receiver type");
		return t;
	}
}
