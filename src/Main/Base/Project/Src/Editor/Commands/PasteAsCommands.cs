﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;

using ICSharpCode.Core;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.CSharp;

namespace ICSharpCode.SharpDevelop.Editor.Commands
{
	public abstract class PasteAsCommand : AbstractMenuCommand
	{
		public override void Run()
		{
			ITextEditor textEditor = SD.GetActiveViewContentService<ITextEditor>();
			if (textEditor == null)
				return;
			
			string clipboardText = SD.Clipboard.GetText();
			if (string.IsNullOrEmpty(clipboardText))
				return;
			
			using (textEditor.Document.OpenUndoGroup())
				Run(textEditor, clipboardText);
		}
		
		protected abstract void Run(ITextEditor editor, string clipboardText);
		
		protected string GetIndentation(IDocument document, int line)
		{
			return DocumentUtilitites.GetWhitespaceAfter(document, document.GetLineByNumber(line).Offset);
		}
	}
	
	/// <summary>
	/// Pastes the clipboard text as a comment.
	/// 
	/// Does the following:
	/// - Take clipboard text
	/// - Get current indentation
	/// - Wrap first line using 'IAmbience.WrapComment'
	/// - If it's too long (according to the column ruler position), word-break
	/// - Insert it
	/// </summary>
	public class PasteAsCommentCommand : PasteAsCommand
	{
		protected override void Run(ITextEditor editor, string clipboardText)
		{
			string indentation = GetIndentation(editor.Document, editor.Caret.Line);
			IAmbience ambience = AmbienceService.GetCurrentAmbience();
			int maxLineLength = editor.Options.VerticalRulerColumn - VisualIndentationLength(editor, indentation);
			StringWriter insertedText = new StringWriter();
			insertedText.NewLine = DocumentUtilitites.GetLineTerminator(editor.Document, editor.Caret.Line);
			using (StringReader reader = new StringReader(clipboardText)) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					AppendTextLine(indentation, ambience, maxLineLength, insertedText, line);
				}
			}
			IDocument document = editor.Document;
			int insertionPos = document.GetLineByNumber(editor.Caret.Line).Offset + indentation.Length;
			document.Insert(insertionPos, insertedText.ToString());
		}
		
		void AppendTextLine(string indentation, IAmbience ambience, int maxLineLength, StringWriter insertedText, string line)
		{
			const int minimumLineLength = 10;
			string commentedLine;
			while (true) {
				commentedLine = ambience.WrapComment(line);
				int commentingOverhead = commentedLine.Length - line.Length;
				if (commentingOverhead < 0 || (maxLineLength - commentingOverhead) < minimumLineLength)
					break;
				if (commentedLine.Length > maxLineLength) {
					int pos = FindWrapPositionBefore(line, maxLineLength - commentingOverhead);
					if (pos < minimumLineLength)
						break;
					insertedText.WriteLine(ambience.WrapComment(line.Substring(0, pos)));
					insertedText.Write(indentation);
					line = line.Substring(pos + 1);
				} else {
					break;
				}
			}
			insertedText.WriteLine(commentedLine);
			insertedText.Write(indentation); // indentation for next line
		}
		
		int FindWrapPositionBefore(string line, int pos)
		{
			return line.LastIndexOf(' ', pos);
		}
		
		int VisualIndentationLength(ITextEditor editor, string indentation)
		{
			int length = 0;
			foreach (char c in indentation) {
				if (c == '\t')
					length += editor.Options.IndentationSize;
				else
					length += 1;
			}
			return length;
		}
	}
	
	/// <summary>
	/// Pastes the clipboard text as a string.
	/// 
	/// Does the following:
	/// - Take clipboard text
	/// - Convert to string literal using CodeGenerator
	/// - Insert it
	/// </summary>
	public class PasteAsStringCommand : PasteAsCommand
	{
		protected override void Run(ITextEditor editor, string clipboardText)
		{
			// TODO: reimplement as C#-specific refactoring with NR5;
			// because CodeDom introduces redundant parentheses
			CodeDomProvider codeDomProvider = null;
			IProject project = ProjectService.CurrentProject;
			if (project != null)
				codeDomProvider = project.CreateCodeDomProvider();
			if (codeDomProvider == null)
				codeDomProvider = new CSharpCodeProvider();
			
			CodeExpression expression = null;
			using (StringReader reader = new StringReader(clipboardText)) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					CodeExpression newExpr = new CodePrimitiveExpression(line);
					if (expression == null) {
						expression = newExpr;
					} else {
						expression = new CodeBinaryOperatorExpression(
							expression,
							CodeBinaryOperatorType.Add,
							new CodePropertyReferenceExpression(
								new CodeTypeReferenceExpression("Environment"),
								"NewLine"
							));
						expression = new CodeBinaryOperatorExpression(
							expression, CodeBinaryOperatorType.Add, newExpr);
						
					}
				}
			}
			if (expression == null)
				return;
			string indentation = GetIndentation(editor.Document, editor.Caret.Line);
			CodeGeneratorOptions options = new CodeGeneratorOptions();
			options.IndentString = editor.Options.IndentationString;
			StringWriter writer = new StringWriter();
			codeDomProvider.GenerateCodeFromExpression(expression, writer, options);
			editor.Document.Insert(editor.Caret.Offset, writer.ToString().Trim());
		}
	}
}
