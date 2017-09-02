﻿/* MIT License

Copyright (c) 2016 Edward Rowe, RedBlueGames

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace RedBlueGames.MulliganRenamer
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Tool that tries to allow renaming mulitple selections by parsing similar substrings
    /// </summary>
    public class MulliganRenamerWindow : EditorWindow
    {
        private const string WindowMenuPath = "Window/Red Blue/Mulligan Renamer";

        private const string AddedTextColorTag = "<color=green>";
        private const string DeletedTextColorTag = "<color=red>";

        private const string RenameOpsEditorPrefsKey = "RedBlueGames.MulliganRenamer.RenameOperationsToApply";
        private const string PreviewModePrefixKey = "RedBlueGames.MulliganRenamer.IsPreviewStepModePreference";

        private const float PreviewPanelFirstColumnMinSize = 50.0f;

        private GUIStyles guiStyles;
        private GUIContents guiContents;
        private Vector2 renameOperationsPanelScrollPosition;
        private Vector2 previewPanelScrollPosition;

        private BulkRenamer BulkRenamer { get; set; }

        private List<RenameOperation> RenameOperationPrototypes { get; set; }

        private List<UnityEngine.Object> ObjectsToRename { get; set; }

        private List<RenameOperation> RenameOperationsToApply { get; set; }

        private RenameOperation OperationToForceFocus { get; set; }

        private int FocusedRenameOpIndex
        {
            get
            {
                var focusedControl = GUI.GetNameOfFocusedControl();
                if (string.IsNullOrEmpty(focusedControl))
                {
                    return -1;
                }

                return GUIControlNameUtility.GetPrefixFromName(focusedControl);
            }
        }

        private RenameOperation FocusedRenameOp
        {
            get
            {
                var focusedOpIndex = this.FocusedRenameOpIndex;
                if (focusedOpIndex >= 0 && focusedOpIndex < this.RenameOperationsToApply.Count)
                {
                    return this.RenameOperationsToApply[this.FocusedRenameOpIndex];
                }
                else
                {
                    return null;
                }
            }
        }

        private bool IsShowingPreviewSteps { get; set; }

        private string LastFocusedControlName { get; set; }

        private bool IsPreviewStepModePreference
        {
            get
            {
                return EditorPrefs.GetBool(PreviewModePrefixKey, true);
            }

            set
            {
                EditorPrefs.SetBool(PreviewModePrefixKey, value);
            }
        }

        [MenuItem(WindowMenuPath, false)]
        private static void ShowRenameSpritesheetWindow()
        {
            var bulkRenamerWindow = EditorWindow.GetWindow<MulliganRenamerWindow>(true, "Mulligan Renamer", true);

            // When they launch via right click, we immediately load the objects in.
            bulkRenamerWindow.LoadSelectedObjects();
        }

        private static bool ObjectIsValidForRename(UnityEngine.Object obj)
        {
            if (AssetDatabase.Contains(obj))
            {
                // Create -> Prefab results in assets that have no name. Typically you can't have Assets that have no name,
                // so we will just ignore them for the utility.
                return !string.IsNullOrEmpty(obj.name);
            }

            if (obj.GetType() == typeof(GameObject))
            {
                return true;
            }

            return false;
        }

        private static int DrawPreviewBreadcrumbs(int selectedIndex, params PreviewBreadcrumbOptions[] breacrumbConfigs)
        {
            var lastSelectedIndex = selectedIndex;
            for (int i = 0; i < breacrumbConfigs.Length; ++i)
            {
                var styleName = i == 0 ? "GUIEditor.BreadcrumbLeft" : "GUIEditor.BreadcrumbMid";
                var enabled = i == lastSelectedIndex;
                bool selected = GUILayout.Toggle(enabled, breacrumbConfigs[i].Heading, styleName);
                if (selected)
                {
                    lastSelectedIndex = i;

                    var coloredHighlightRect = GUILayoutUtility.GetLastRect();
                    coloredHighlightRect.height = 2;
                    coloredHighlightRect.x += -5.0f;
                    var oldColor = GUI.color;
                    GUI.color = breacrumbConfigs[i].HighlightColor;
                    GUI.DrawTexture(coloredHighlightRect, Texture2D.whiteTexture);
                    GUI.color = oldColor;
                }
            }

            return lastSelectedIndex;
        }

        private static bool DrawPreviewRow(Rect rowRect, int previewStepIndex, PreviewRowModel info, PreviewRowStyle style)
        {
            bool isDeleteClicked = false;

            var originalName = previewStepIndex >= 0 && previewStepIndex < info.RenameResultSequence.NumSteps ? 
                info.RenameResultSequence.GetOriginalNameAtStep(previewStepIndex, style.DeletionColor) : 
                info.RenameResultSequence.OriginalName;
            GUI.Label(rowRect, originalName, style.FirstColumnStyle);

            /*
            var horizontalRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(18.0f));

            var oldColor = GUI.color;
            GUI.color = style.BackgroundColor;
            GUI.DrawTexture(horizontalRect, Texture2D.whiteTexture);
            GUI.color = oldColor;

            // Space gives us a bit of padding or else we're just too bunched up to the side
            GUILayout.Space(20.0f);

            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(16.0f)))
            {
                isDeleteClicked = true;
            }

            GUILayout.Box(info.Icon, style.IconStyle, GUILayout.Width(16.0f), GUILayout.Height(16.0f));

            if (style.FirstColumnWidth > 0)
            {
                var originalName = previewStepIndex >= 0 && previewStepIndex < info.RenameResultSequence.NumSteps ? 
                    info.RenameResultSequence.GetOriginalNameAtStep(previewStepIndex, style.DeletionColor) : 
                    info.RenameResultSequence.OriginalName;
                EditorGUILayout.LabelField(originalName, style.FirstColumnStyle, GUILayout.Width(style.FirstColumnWidth));
            }

            if (style.SecondColumnWidth > 0)
            {
                var newName = previewStepIndex >= 0 && previewStepIndex < info.RenameResultSequence.NumSteps ? 
                    info.RenameResultSequence.GetNewNameAtStep(previewStepIndex, style.InsertionColor) : 
                    info.RenameResultSequence.NewName;
                EditorGUILayout.LabelField(newName, style.SecondColumnStyle, GUILayout.Width(style.SecondColumnWidth));
            }

            if (style.ThirdColumnWidth > 0)
            {
                EditorGUILayout.LabelField(info.RenameResultSequence.NewName, style.ThirdColumnStyle, GUILayout.Width(style.ThirdColumnWidth));
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            */
            return isDeleteClicked;
        }

        private void OnEnable()
        {
            AssetPreview.SetPreviewTextureCacheSize(100);
            this.minSize = new Vector2(600.0f, 300.0f);

            this.previewPanelScrollPosition = Vector2.zero;

            this.BulkRenamer = new BulkRenamer();
            this.RenameOperationsToApply = new List<RenameOperation>();
            this.ObjectsToRename = new List<UnityEngine.Object>();

            this.CacheRenameOperationPrototypes();
            this.LoadSavedRenameOperations();

            Selection.selectionChanged += this.Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= this.Repaint;
        }

        private void CacheRenameOperationPrototypes()
        {
            this.RenameOperationPrototypes = new List<RenameOperation>();

            this.RenameOperationPrototypes.Add(new ReplaceStringOperation());
            this.RenameOperationPrototypes.Add(new ReplaceNameOperation());
            this.RenameOperationPrototypes.Add(new AddStringOperation());
            this.RenameOperationPrototypes.Add(new EnumerateOperation());
            this.RenameOperationPrototypes.Add(new TrimCharactersOperation());
            this.RenameOperationPrototypes.Add(new RemoveCharactersOperation());
            this.RenameOperationPrototypes.Add(new ChangeCaseOperation());
        }

        private void InitializeGUIContents()
        {
            this.guiContents = new GUIContents();

            this.guiContents.DropPrompt = new GUIContent(
                "No objects specified for rename. Drag objects here to rename them, or");

            this.guiContents.DropPromptHint = new GUIContent(
                "Add more objects by dragging them here");
        }

        private void InitializeGUIStyles()
        {
            this.guiStyles = new GUIStyles();

            this.guiStyles.Icon = GUIStyle.none;
            this.guiStyles.OriginalNameLabelUnModified = EditorStyles.label;
            this.guiStyles.OriginalNameLabelUnModified.richText = true;

            this.guiStyles.OriginalNameLabelWhenModified = EditorStyles.boldLabel;
            this.guiStyles.OriginalNameLabelWhenModified.richText = true;

            this.guiStyles.NewNameLabelUnModified = EditorStyles.label;
            this.guiStyles.NewNameLabelUnModified.richText = true;

            this.guiStyles.NewNameLabelModified = EditorStyles.boldLabel;
            this.guiStyles.NewNameLabelModified.richText = true;

            this.guiStyles.FinalNameLabelUnModified = EditorStyles.label;
            this.guiStyles.FinalNameLabelUnModified.richText = true;

            this.guiStyles.FinalNameLabelWhenModified = EditorStyles.boldLabel;
            this.guiStyles.FinalNameLabelWhenModified.richText = true;

            this.guiStyles.DropPrompt = new GUIStyle(EditorStyles.label);
            this.guiStyles.DropPrompt.alignment = TextAnchor.MiddleCenter;
            this.guiStyles.DropPromptHint = EditorStyles.centeredGreyMiniLabel;

            var previewHeaderStyle = new GUIStyle(EditorStyles.toolbar);
            var previewHeaderMargin = new RectOffset();
            previewHeaderMargin = previewHeaderStyle.margin;
            previewHeaderMargin.left = 1;
            previewHeaderMargin.right = 1;
            previewHeaderStyle.margin = previewHeaderMargin;
            this.guiStyles.PreviewHeader = previewHeaderStyle;

            if (EditorGUIUtility.isProSkin)
            {
                this.guiStyles.PreviewScroll = new GUIStyle("CurveEditorBackground");

                this.guiStyles.PreviewRowBackgroundEven = new Color(0.3f, 0.3f, 0.3f, 0.2f);

                this.guiStyles.InsertionTextColor = new Color32(6, 214, 160, 255);
                this.guiStyles.DeletionTextColor = new Color32(239, 71, 111, 255);
            }
            else
            {
                this.guiStyles.PreviewScroll = EditorStyles.textArea;

                this.guiStyles.PreviewRowBackgroundEven = new Color(0.6f, 0.6f, 0.6f, 0.2f);

                this.guiStyles.InsertionTextColor = new Color32(0, 140, 104, 255);
                this.guiStyles.DeletionTextColor = new Color32(189, 47, 79, 255);
            }

            this.guiStyles.PreviewRowBackgroundOdd = Color.clear;
        }

        private void OnGUI()
        {
            // Initialize GUIContents and GUIStyles in OnGUI since it makes calls that must be done in OnGUI loop.
            if (this.guiContents == null)
            {
                this.InitializeGUIContents();
            }

            if (this.guiStyles == null)
            {
                this.InitializeGUIStyles();
            }

            // Remove any objects that got deleted while working
            this.ObjectsToRename.RemoveNullObjects();
            
            this.DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            this.DrawOperationsPanel();

            this.FocusForcedFocusControl();

            this.DrawPreviewPanel();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(30.0f);

            var disableRenameButton = this.RenameOperatationsHaveErrors() || this.ObjectsToRename.Count == 0;
            EditorGUI.BeginDisabledGroup(disableRenameButton);
            if (GUILayout.Button("Rename", GUILayout.Height(24.0f)))
            {
                this.BulkRenamer.RenameObjects(this.ObjectsToRename);
                this.ObjectsToRename.Clear();
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(30.0f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Issue #115 - Workaround to force focus to stay with whatever widget it was previously on...
            var focusedControl = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(focusedControl))
            {
                GUI.FocusControl(this.LastFocusedControlName);
                EditorGUI.FocusTextInControl(this.LastFocusedControlName);
            }
            else
            {
                this.LastFocusedControlName = GUI.GetNameOfFocusedControl();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Rename Operations", GUILayout.Width(340.0f));

            // The breadcrumb style spills to the left some so we need to leave extra space for it
            const float BreadcrumbLeftOffset = 5.0f;
            GUILayout.Space(BreadcrumbLeftOffset + 1.0f);

            // Show step previewing mode when only one operation is left because Results mode is pointless with one op only.
            // But don't actually change the mode preference so that adding ops restores whatever mode the user was in.
            this.IsShowingPreviewSteps = this.IsPreviewStepModePreference || this.RenameOperationsToApply.Count <= 1;
            if (this.IsShowingPreviewSteps)
            {
                var breadcrumbOptions = new PreviewBreadcrumbOptions[this.RenameOperationsToApply.Count];
                for (int i = 0; i < this.RenameOperationsToApply.Count; ++i)
                {
                    breadcrumbOptions[i].Heading = this.RenameOperationsToApply[i].HeadingLabel;
                    breadcrumbOptions[i].HighlightColor = this.RenameOperationsToApply[i].HighlightColor;
                }

                var selectedBreadcrumbIndex = DrawPreviewBreadcrumbs(this.FocusedRenameOpIndex, breadcrumbOptions);
                if (selectedBreadcrumbIndex != this.FocusedRenameOpIndex)
                {
                    var renameOp = this.RenameOperationsToApply[selectedBreadcrumbIndex];
                    this.FocusRenameOperationDeferred(renameOp);
                }
            }
            else
            {
                DrawPreviewBreadcrumbs(0, new PreviewBreadcrumbOptions() { Heading = "Result", HighlightColor = Color.clear });
            }

            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(this.RenameOperationsToApply.Count <= 1);
            var buttonText = "Preview Steps";
            this.IsPreviewStepModePreference = GUILayout.Toggle(this.IsPreviewStepModePreference, buttonText, "toolbarbutton");
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawOperationsPanel()
        {
            EditorGUILayout.BeginVertical();

            this.renameOperationsPanelScrollPosition = EditorGUILayout.BeginScrollView(
                this.renameOperationsPanelScrollPosition,
                GUILayout.Width(350.0f));

            this.DrawRenameOperations();

            // BulkRenamer expects the list typed as IRenameOperations
            var renameOpsAsInterfaces = new List<IRenameOperation>();
            foreach (var renameOp in this.RenameOperationsToApply)
            {
                renameOpsAsInterfaces.Add((IRenameOperation)renameOp);
            }

            this.BulkRenamer.SetRenameOperations(renameOpsAsInterfaces);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add Operation", GUILayout.Width(150.0f)))
            {
                // Add enums to the menu
                var menu = new GenericMenu();
                for (int i = 0; i < this.RenameOperationPrototypes.Count; ++i)
                {
                    var renameOp = this.RenameOperationPrototypes[i];
                    var content = new GUIContent(renameOp.MenuDisplayPath);
                    menu.AddItem(content, false, this.OnAddRenameOperationConfirmed, renameOp);
                }

                menu.ShowAsContext();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRenameOperations()
        {
            // Store the op before buttons are pressed because buttons change focus
            var focusedOpBeforeButtonPresses = this.FocusedRenameOp;

            for (int i = 0; i < this.RenameOperationsToApply.Count; ++i)
            {
                var currentElement = this.RenameOperationsToApply[i];
                var guiOptions = new RenameOperation.GUIOptions();
                guiOptions.ControlPrefix = i;
                guiOptions.DisableUpButton = i == 0;
                guiOptions.DisableDownButton = i == this.RenameOperationsToApply.Count - 1;
                var buttonClickEvent = currentElement.DrawGUI(guiOptions);
                switch (buttonClickEvent)
                {
                    case RenameOperation.ListButtonEvent.MoveUp:
                        {
                            this.MoveRenameOpFromIndexToIndex(i, i - 1);

                            // Move focus with the RenameOp. This techincally changes their focus within the 
                            // rename op, but it's better than focus getting swapped to whatever op replaces this one.
                            this.FocusRenameOperationDeferred(focusedOpBeforeButtonPresses);
                            break;
                        }

                    case RenameOperation.ListButtonEvent.MoveDown:
                        {
                            this.MoveRenameOpFromIndexToIndex(i, i + 1);
                            this.FocusRenameOperationDeferred(focusedOpBeforeButtonPresses);
                            break;
                        }

                    case RenameOperation.ListButtonEvent.Delete:
                        {
                            var removingFocusedOperation = focusedOpBeforeButtonPresses == currentElement;

                            this.RemoveRenameOperationAt(i);

                            if (removingFocusedOperation && this.RenameOperationsToApply.Count > 0)
                            {
                                // Focus the RenameOp that took this one's place, if there is one. 
                                var indexToFocus = Mathf.Min(this.RenameOperationsToApply.Count - 1, i);
                                this.FocusRenameOperationDeferred(this.RenameOperationsToApply[indexToFocus]);
                            }
                            else
                            {
                                this.FocusRenameOperationDeferred(focusedOpBeforeButtonPresses);
                            }

                            break;
                        }

                    case RenameOperation.ListButtonEvent.None:
                        {
                            // Do nothing
                            break;
                        }

                    default:
                        {
                            Debug.LogError(string.Format(
                                    "RenamerWindow found Unrecognized ListButtonEvent [{0}] in OnGUI. Add a case to handle this event.", 
                                    buttonClickEvent));
                            return;
                        }
                }
            }
        }

        private void DrawPreviewPanel()
        {
            bool panelIsEmpty = this.ObjectsToRename.Count == 0;
            float remainingFooterHeight = panelIsEmpty ? 80 : 120;
            var absoluteScrollRect = GUILayoutUtility.GetRect(this.position.width - 350, this.position.height - remainingFooterHeight);

            GUI.BeginGroup(absoluteScrollRect, this.guiStyles.PreviewScroll);

            var relativeScrollRect = new Rect(absoluteScrollRect);
            relativeScrollRect.position = Vector2.zero;
            var internalScrollArea = panelIsEmpty ? Rect.zero : new Rect(0, 0, absoluteScrollRect.width, 300 * 16);
            this.previewPanelScrollPosition = GUI.BeginScrollView(
                relativeScrollRect,
                this.previewPanelScrollPosition,
                internalScrollArea);

            if (panelIsEmpty)
            {
                this.DrawPreviewPanelContentsEmpty(relativeScrollRect);
            }
            else
            {
                this.DrawPreviewPanelContentsWithItems(relativeScrollRect);
            }

            GUI.EndScrollView();
            GUI.EndGroup();

            var draggedObjects = this.GetDraggedObjectsOverRect(absoluteScrollRect);
            if (draggedObjects.Count > 0)
            {
                this.AddObjectsToRename(draggedObjects);
                this.ScrollPreviewPanelToBottom();
            }

            if (!panelIsEmpty)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove All"))
                {
                    this.ObjectsToRename.Clear();
                }

                this.DrawAddSelectedObjectsButton();

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPreviewPanelContentsEmpty(Rect relativeRect)
        {
            this.guiStyles.DropPrompt.wordWrap = true;

            // TODO: DOESN WORK IN A TINY WINDOW, YET
            float contentPadding = 50.0f;
            var labelSize = Vector2.zero;
            labelSize.x = relativeRect.width - contentPadding;
            labelSize.y = this.guiStyles.DropPrompt.CalcHeight(new GUIContent(this.guiContents.DropPrompt), relativeRect.width);
            var labelRect = new Rect(relativeRect.center - (labelSize * 0.5f), labelSize);
            GUI.Label(labelRect, this.guiContents.DropPrompt, this.guiStyles.DropPrompt);

            // TODO: MEASURE THE BUTTON TOO - FOR MULTI LANGUAGE SUPPORT
            var buttonSize = new Vector2(150.0f, 16.0f);
            var buttonPosition = new Vector2(relativeRect.center.x - buttonSize.x * 0.5f, labelRect.yMax);
            var buttonRect = new Rect(buttonPosition, buttonSize);
            this.DrawAddSelectedObjectsButton(buttonRect);

            return;
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(this.guiContents.DropPrompt, this.guiStyles.DropPrompt);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            this.DrawAddSelectedObjectsButton();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        private void DrawPreviewPanelContentsWithItems(Rect panelRect)
        {
            var headerHeight = 16.0f;
            float elementHeight = 16.0f;
            var index = (int)Mathf.Min((this.previewPanelScrollPosition.y / elementHeight), this.ObjectsToRename.Count - 1);
            int numToGrab = Mathf.Min(30, this.ObjectsToRename.Count - index);
            var visibleObjects = this.ObjectsToRename.GetRange(index, numToGrab);

            var previewContents = PreviewPanelContents.CreatePreviewContentsForObjects(this.BulkRenamer, visibleObjects);

            // Space gives us a bit of padding or else we're just too bunched up to the side
            var paddingLeft = 42.0f;

            int renameStep = this.IsShowingPreviewSteps ? this.FocusedRenameOpIndex : -1;
            string originalNameColumnHeader = renameStep < 1 ? "Original" : "Before";
            string newNameColumnHeader = "After";

            var firstColumnRect = new Rect(paddingLeft, previewPanelScrollPosition.y, previewContents.LongestOriginalNameWidth, headerHeight);
            GUI.Label(firstColumnRect, originalNameColumnHeader, EditorStyles.boldLabel);
            //EditorGUILayout.LabelField(originalNameColumnHeader, EditorStyles.boldLabel, GUILayout.Width(previewContents.LongestOriginalNameWidth));


            bool shouldShowSecondColumn = this.IsPreviewStepModePreference;
            if (shouldShowSecondColumn)
            {
                //EditorGUILayout.LabelField(newNameColumnHeader, EditorStyles.boldLabel, GUILayout.Width(previewContents.LongestNewNameWidth));
            }


            bool shouldShowThirdColumn = !this.IsShowingPreviewSteps || this.RenameOperationsToApply.Count > 1;
            if (shouldShowThirdColumn)
            {
                //EditorGUILayout.LabelField("Final Name", EditorStyles.boldLabel, GUILayout.Width(previewContents.LongestFinalNameWidth));
            }

            //GUILayout.FlexibleSpace();

            //EditorGUILayout.EndHorizontal();

            var rowsRect = new Rect(panelRect);
            rowsRect.y = previewPanelScrollPosition.y + headerHeight;
            this.DrawPreviewRows(rowsRect, renameStep, previewContents, shouldShowSecondColumn, shouldShowThirdColumn);

            //GUILayout.FlexibleSpace();
            //EditorGUILayout.LabelField(this.guiContents.DropPromptHint, this.guiStyles.DropPromptHint);
        }

        private void DrawPreviewRows(Rect drawRect, int stepIndex, PreviewPanelContents previewContents, bool showSecondColumn, bool showThirdColumn)
        {
            for (int i = 0; i < previewContents.NumRows; ++i)
            {
                var content = previewContents[i];
                var previewRowStyle = new PreviewRowStyle();
                previewRowStyle.IconStyle = this.guiStyles.Icon;

                previewRowStyle.FirstColumnStyle = content.NamesAreDifferent ? 
                    this.guiStyles.OriginalNameLabelWhenModified : 
                    this.guiStyles.OriginalNameLabelUnModified;
                previewRowStyle.FirstColumnWidth = previewContents.LongestOriginalNameWidth;

                previewRowStyle.SecondColumnStyle = content.NamesAreDifferent ? 
                    this.guiStyles.NewNameLabelModified : 
                    this.guiStyles.NewNameLabelUnModified;

                previewRowStyle.SecondColumnWidth = showSecondColumn ? previewContents.LongestNewNameWidth : 0.0f;

                previewRowStyle.ThirdColumnStyle = content.NamesAreDifferent ? 
                    this.guiStyles.FinalNameLabelWhenModified : 
                    this.guiStyles.FinalNameLabelUnModified;

                previewRowStyle.ThirdColumnWidth = showThirdColumn ? previewContents.LongestFinalNameWidth : 0.0f;

                previewRowStyle.BackgroundColor = i % 2 == 0 ? this.guiStyles.PreviewRowBackgroundEven : this.guiStyles.PreviewRowBackgroundOdd;

                previewRowStyle.InsertionColor = this.guiStyles.InsertionTextColor;
                previewRowStyle.DeletionColor = this.guiStyles.DeletionTextColor;

                var rowHeight = 16.0f;
                var rowRect = new Rect(0, drawRect.y + i * rowHeight, drawRect.width, rowHeight);
                if (DrawPreviewRow(rowRect, stepIndex, content, previewRowStyle))
                {
                    this.ObjectsToRename.Remove(this.ObjectsToRename[i]);
                    break;
                }
            }
        }

        private void DrawAddSelectedObjectsButton(Rect rect)
        {
            var newlySelectedObjects = this.GetValidSelectedObjects();
            EditorGUI.BeginDisabledGroup(newlySelectedObjects.Count == 0);
            if (GUI.Button(rect, "Add Selected Objects"))
            {
                this.LoadSelectedObjects();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawAddSelectedObjectsButton()
        {
            var newlySelectedObjects = this.GetValidSelectedObjects();
            EditorGUI.BeginDisabledGroup(newlySelectedObjects.Count == 0);
            if (GUILayout.Button("Add Selected Objects"))
            {
                this.LoadSelectedObjects();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void MoveRenameOpFromIndexToIndex(int fromIndex, int desiredIndex)
        {
            var oldFocusedOp = this.FocusedRenameOp;
            desiredIndex = Mathf.Clamp(desiredIndex, 0, this.RenameOperationsToApply.Count - 1);
            var destinationElementCopy = this.RenameOperationsToApply[desiredIndex];
            this.RenameOperationsToApply[desiredIndex] = this.RenameOperationsToApply[fromIndex];
            this.RenameOperationsToApply[fromIndex] = destinationElementCopy;

            this.SaveRenameOperationsToPreferences();
        }

        private void OnAddRenameOperationConfirmed(object operation)
        {
            var operationAsRenameOp = operation as RenameOperation;
            if (operationAsRenameOp == null)
            {
                throw new System.ArgumentException(
                    "MulliganRenamerWindow tried to add a new RenameOperation using a type that is not a subclass of BaseRenameOperation." +
                    " Operation type: " +
                    operation.GetType().ToString());
            }

            this.AddRenameOperation(operationAsRenameOp);
        }

        private void AddRenameOperation(RenameOperation operation)
        {
            // Construct the Rename op
            var renameOp = operation.Clone();
            this.RenameOperationsToApply.Add(renameOp);

            this.SaveRenameOperationsToPreferences();

            // Scroll to the bottom to focus the newly created operation.
            this.ScrollRenameOperationsToBottom();

            this.FocusRenameOperationDeferred(renameOp);
        }

        private void RemoveRenameOperationAt(int indexToRemove)
        {
            this.RenameOperationsToApply.RemoveAt(indexToRemove);
            this.SaveRenameOperationsToPreferences();
        }

        private void SaveRenameOperationsToPreferences()
        {
            var allOpPathsCommaSeparated = string.Empty;
            foreach (var op in this.RenameOperationsToApply)
            {
                allOpPathsCommaSeparated += op.MenuDisplayPath;
                if (op != this.RenameOperationsToApply.Last())
                {
                    allOpPathsCommaSeparated += ",";
                }
            }

            EditorPrefs.SetString(RenameOpsEditorPrefsKey, allOpPathsCommaSeparated);
        }

        private void LoadSavedRenameOperations()
        {
            var serializedOps = EditorPrefs.GetString(RenameOpsEditorPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(serializedOps))
            {
                this.RenameOperationsToApply.Add(new ReplaceStringOperation());
            }
            else
            {
                var ops = serializedOps.Split(',');
                foreach (var op in ops)
                {
                    foreach (var prototypeOp in this.RenameOperationPrototypes)
                    {
                        if (prototypeOp.MenuDisplayPath == op)
                        {
                            this.AddRenameOperation(prototypeOp);
                            break;
                        }
                    }
                }
            }

            if (this.RenameOperationsToApply.Count > 0)
            {
                this.FocusRenameOperationDeferred(this.RenameOperationsToApply.First());
            }
        }

        private void FocusRenameOperationDeferred(RenameOperation renameOperation)
        {
            this.OperationToForceFocus = renameOperation;
        }

        private void FocusForcedFocusControl()
        {
            if (this.OperationToForceFocus == null)
            {
                return;
            }

            var controlNameToForceFocus = string.Empty; 
            for (int i = 0; i < this.RenameOperationsToApply.Count; ++i)
            {
                if (this.RenameOperationsToApply[i] == this.OperationToForceFocus)
                {
                    controlNameToForceFocus = GUIControlNameUtility.CreatePrefixedName(
                        i, 
                        this.RenameOperationsToApply[i].ControlToFocus);
                    break;
                }
            }

            if (!string.IsNullOrEmpty(controlNameToForceFocus))
            {
                var previouslyFocusedControl = GUI.GetNameOfFocusedControl();

                // Try to focus the desired control
                GUI.FocusControl(controlNameToForceFocus);
                EditorGUI.FocusTextInControl(controlNameToForceFocus);

                // Stop focusing the desired control only once it's been focused.
                // (Workaround because for some reason this fails to focus a control when users click between breadcrumbs)
                var focusedControl = GUI.GetNameOfFocusedControl();
                if (controlNameToForceFocus.Equals(focusedControl))
                {
                    this.FocusRenameOperationDeferred(null);
                }
                else
                {
                    // If we weren't able to focus the new control, go back to whatever was focused before.
                    GUI.FocusControl(previouslyFocusedControl);
                    EditorGUI.FocusTextInControl(previouslyFocusedControl);
                }
            }
        }

        private List<UnityEngine.Object> GetDraggedObjectsOverRect(Rect dropArea)
        {
            Event currentEvent = Event.current;

            var droppedObjects = new List<UnityEngine.Object>();
            if (!dropArea.Contains(currentEvent.mousePosition))
            {
                return droppedObjects;
            }

            var validDraggedObjects = this.GetValidObjectsForRenameFromGroup(DragAndDrop.objectReferences);
            var isDraggingValidAssets = (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform) &&
                                        validDraggedObjects.Count > 0;
            if (isDraggingValidAssets)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (currentEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    droppedObjects.AddRange(validDraggedObjects);
                }

                currentEvent.Use();
            }

            return droppedObjects;
        }

        private void LoadSelectedObjects()
        {
            this.AddObjectsToRename(this.GetValidSelectedObjects());

            // Scroll to the bottom to focus the newly added objects.
            this.ScrollPreviewPanelToBottom();
        }

        private void AddObjectsToRename(List<UnityEngine.Object> objectsToAdd)
        {
            // Sort the objects before adding them
            var assets = new List<UnityEngine.Object>();
            var gameObjects = new List<UnityEngine.Object>();
            foreach (var obj in objectsToAdd)
            {
                if (obj.IsAsset())
                {
                    assets.Add(obj);
                }
                else
                {
                    gameObjects.Add((GameObject)obj);
                }
            }

            // When clicking and dragging from the scene, GameObjects are properly sorted according to the hierarchy.
            // But when selected and adding them, they are not. So we need to resort them here.
            gameObjects.Sort((x, y) => ((GameObject)x).GetHierarchySorting().CompareTo(((GameObject)y).GetHierarchySorting()));

            assets.Sort((x, y) =>
                {
                    return EditorUtility.NaturalCompare(x.name, y.name);
                });

            this.ObjectsToRename.AddRange(assets);
            this.ObjectsToRename.AddRange(gameObjects);
        }

        private List<UnityEngine.Object> GetValidSelectedObjects()
        {
            return this.GetValidObjectsForRenameFromGroup(Selection.objects);
        }

        private List<UnityEngine.Object> GetValidObjectsForRenameFromGroup(ICollection<UnityEngine.Object> objects)
        {
            var validObjects = new List<UnityEngine.Object>();
            foreach (var selectedObject in objects)
            {
                if (ObjectIsValidForRename(selectedObject) && !this.ObjectsToRename.Contains(selectedObject))
                {
                    validObjects.Add(selectedObject);
                }
            }

            return validObjects;
        }

        private bool RenameOperatationsHaveErrors()
        {
            foreach (var renameOp in this.RenameOperationsToApply)
            {
                if (renameOp.HasErrors)
                {
                    return true;
                }
            }

            return false;
        }

        private void ScrollPreviewPanelToBottom()
        {
            this.previewPanelScrollPosition = new Vector2(0.0f, 100000);
        }

        private void ScrollRenameOperationsToBottom()
        {
            this.renameOperationsPanelScrollPosition = new Vector2(0.0f, 100000);
        }

        private struct PreviewBreadcrumbOptions
        {
            public string Heading { get; set; }

            public Color32 HighlightColor { get; set; }
        }

        private struct PreviewRowModel
        {
            public Texture Icon { get; set; }

            public RenameResultSequence RenameResultSequence { get; set; }

            public bool NamesAreDifferent
            {
                get
                {
                    return this.RenameResultSequence.OriginalName != this.RenameResultSequence.NewName;
                }
            }
        }

        private struct PreviewRowStyle
        {
            public GUIStyle IconStyle { get; set; }

            public Color DeletionColor { get; set; }

            public Color InsertionColor { get; set; }

            public GUIStyle FirstColumnStyle { get; set; }

            public float FirstColumnWidth { get; set; }

            public GUIStyle SecondColumnStyle { get; set; }

            public float SecondColumnWidth { get; set; }

            public GUIStyle ThirdColumnStyle { get; set; }

            public float ThirdColumnWidth { get; set; }

            public Color BackgroundColor { get; set; }
        }

        private class PreviewPanelContents
        {
            private const float MinColumnWidth = 150.0f;

            public float LongestOriginalNameWidth { get; private set; }

            public float LongestNewNameWidth { get; private set; }

            public float LongestFinalNameWidth { get; private set; }

            public int NumRows
            {
                get
                {
                    return this.PreviewRowInfos.Length;
                }
            }

            private PreviewRowModel[] PreviewRowInfos { get; set; }

            public PreviewRowModel this [int index]
            {
                get
                {
                    if (index >= 0 && index < this.PreviewRowInfos.Length)
                    {
                        return this.PreviewRowInfos[index];
                    }
                    else
                    {
                        throw new System.IndexOutOfRangeException(
                            "Trying to access PreviewRowModel at index that is out of bounds. Index: " + index);
                    }
                }
            }

            public static PreviewPanelContents CreatePreviewContentsForObjects(BulkRenamer renamer, List<UnityEngine.Object> objects)
            {
                var preview = new PreviewPanelContents();
                preview.PreviewRowInfos = new PreviewRowModel[objects.Count];
                var objectNames = objects.GetNames();
                var namePreviews = renamer.GetRenamePreviews(objectNames);

                for (int i = 0; i < namePreviews.Count; ++i)
                {
                    var info = new PreviewRowModel();
                    var namePreview = namePreviews[i];
                    info.RenameResultSequence = namePreview;

                    info.Icon = GetIconForObject(objects[i]);

                    preview.PreviewRowInfos[i] = info;
                }

                preview.LongestOriginalNameWidth = 0.0f;
                preview.LongestNewNameWidth = 0.0f;
                foreach (var previewRowInfo in preview.PreviewRowInfos)
                {
                    float originalNameWidth = EditorStyles.boldLabel.CalcSize(new GUIContent(previewRowInfo.RenameResultSequence.OriginalName)).x;
                    if (originalNameWidth > preview.LongestOriginalNameWidth)
                    {
                        preview.LongestOriginalNameWidth = originalNameWidth;
                    }

                    float newNameWidth = EditorStyles.boldLabel.CalcSize(new GUIContent(previewRowInfo.RenameResultSequence.NewName)).x;
                    if (newNameWidth > preview.LongestNewNameWidth)
                    {
                        preview.LongestNewNameWidth = newNameWidth;
                    }
                }

                preview.LongestOriginalNameWidth = Mathf.Max(MinColumnWidth, preview.LongestOriginalNameWidth);
                preview.LongestNewNameWidth = Mathf.Max(MinColumnWidth, preview.LongestNewNameWidth);
                preview.LongestFinalNameWidth = preview.LongestNewNameWidth;

                return preview;
            }

            private static Texture GetIconForObject(UnityEngine.Object unityObject)
            {
                var pathToObject = AssetDatabase.GetAssetPath(unityObject);
                Texture icon = null;
                if (string.IsNullOrEmpty(pathToObject))
                {
                    if (unityObject.GetType() == typeof(GameObject))
                    {
                        icon = EditorGUIUtility.FindTexture("GameObject Icon");
                    }
                    else
                    {
                        icon = EditorGUIUtility.FindTexture("DefaultAsset Icon");
                    }
                }
                else
                {
                    if (unityObject is Sprite)
                    {
                        icon = AssetPreview.GetAssetPreview(unityObject);
                    }
                    else
                    {
                        icon = AssetDatabase.GetCachedIcon(pathToObject);
                    }
                }

                return icon;
            }
        }

        private class GUIStyles
        {
            public GUIStyle PreviewScroll { get; set; }

            public GUIStyle Icon { get; set; }

            public GUIStyle OriginalNameLabelUnModified { get; set; }

            public GUIStyle OriginalNameLabelWhenModified { get; set; }

            public GUIStyle NewNameLabelUnModified { get; set; }

            public GUIStyle NewNameLabelModified { get; set; }

            public GUIStyle FinalNameLabelUnModified { get; set; }

            public GUIStyle FinalNameLabelWhenModified { get; set; }

            public GUIStyle DropPrompt { get; set; }

            public GUIStyle DropPromptHint { get; set; }

            public GUIStyle PreviewHeader { get; set; }

            public Color PreviewRowBackgroundOdd { get; set; }

            public Color PreviewRowBackgroundEven { get; set; }

            public Color InsertionTextColor { get; set; }

            public Color DeletionTextColor { get; set; }
        }

        private class GUIContents
        {
            public GUIContent DropPrompt { get; set; }

            public GUIContent DropPromptHint { get; set; }
        }
    }
}
