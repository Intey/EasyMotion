using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using System.Windows;

namespace EasyMotion.Implementation.Adornment
{
    internal sealed class EasyMotionAdornmentController : IEasyMotionNavigator
    {
        private static readonly string[] NavigationKeys =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Select(x => x.ToString())
            .ToArray();

        private readonly IEasyMotionUtil _easyMotionUtil;
        private readonly IWpfTextView _wpfTextView;
        private readonly IEditorFormatMap _editorFormatMap;
        private readonly IClassificationFormatMap _classificationFormatMap;
        private readonly Dictionary<string, SnapshotPoint> _navigateMap = new Dictionary<string, SnapshotPoint>();

        private readonly Dictionary<string, List<SnapshotPoint>> _navigateGroups = new Dictionary<string, List<SnapshotPoint>>();

        private readonly object _tag = new object();
        private IAdornmentLayer _adornmentLayer;

        internal EasyMotionAdornmentController(IEasyMotionUtil easyMotionUtil, IWpfTextView wpfTextview, IEditorFormatMap editorFormatMap, IClassificationFormatMap classificationFormatMap)
        {
            _easyMotionUtil = easyMotionUtil;
            _wpfTextView = wpfTextview;
            _editorFormatMap = editorFormatMap;
            _classificationFormatMap = classificationFormatMap;
        }

        internal void SetAdornmentLayer(IAdornmentLayer adornmentLayer)
        {
            Debug.Assert(_adornmentLayer == null);
            _adornmentLayer = adornmentLayer;
            Subscribe();
        }
        private void Subscribe()
        {
            _easyMotionUtil.StateChanged += OnStateChanged;
            _wpfTextView.LayoutChanged += OnLayoutChanged;
        }
        private void Unsubscribe()
        {
            _easyMotionUtil.StateChanged -= OnStateChanged;
            _wpfTextView.LayoutChanged -= OnLayoutChanged;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (_easyMotionUtil.State == EasyMotionState.LookingForDecision)

                AddAdornmentsForPage();
            else
                clearAdornmentLayer();
        }

        private void clearAdornmentLayer()
        {
            _adornmentLayer.RemoveAdornmentsByTag(_tag);
        }

        private void OnLayoutChanged(object sender, EventArgs e)
        {
            switch (_easyMotionUtil.State)
            {
                case EasyMotionState.LookingCharNotFound:
                    _easyMotionUtil.ChangeToLookingForDecision(_easyMotionUtil.TargetChar);
                    break;

                case EasyMotionState.LookingForDecision:
                    ResetAdornments();
                    break;
            }
        }

        private void ResetAdornments()
        {
            _adornmentLayer.RemoveAdornmentsByTag(_tag);

            foreach (var keypoint in _navigateMap)
                addAdornmentPoint(keypoint);
            foreach (var group in _navigateGroups)
                foreach (var point in group.Value)
                    addAdornmentPoint(group.Key, point);
        }
        
        private void addAdornmentPoint(KeyValuePair<string, SnapshotPoint> point)
        {
            addAdornmentPoint(point.Key, point.Value);
        }
        private void addAdornmentPoint(string key, SnapshotPoint point)
        {
            var hotSpotUI = CreateHotSpotUI(point, key); 
            if (hotSpotUI == null) return;
            _adornmentLayer.AddAdornment(new SnapshotSpan(point, 1), _tag, hotSpotUI);
        }

        private void AddAdornments()
        {
            Debug.Assert(_easyMotionUtil.State == EasyMotionState.LookingForDecision);


            if (_wpfTextView.InLayout) { return; }
            _navigateMap.Clear();

            var textViewLines = _wpfTextView.TextViewLines;
            //create point below caret
            var startPoint = _wpfTextView.Caret.Position.BufferPosition; //textViewLines.FirstVisibleLine.Start;
            var endPoint = textViewLines.LastVisibleLine.End;

            AddAdornments(startPoint, endPoint);

        }

        private void AddAdornments(SnapshotPoint startPoint, SnapshotPoint endPoint)
        {
            var snapshot = startPoint.Snapshot;
            int navigateIndex = 0;
            int ss_count = startPoint.Position - endPoint.Position;

            if (ss_count > NavigationKeys.Length) 
            {
                /*TODO: create groups */
            }                                        
            /* CONCEPT:
             * Creating places(hotspot) for step with hotkey depends on dictionary. 
             * When count of points(that needs to be marked with hotspot) greater
             * then dictionary length, should use grouping.
             */

            int hotspots_c = endPoint.Position - startPoint.Position;
            int keys_c = NavigationKeys.Length;
            int groups_count = (hotspots_c - keys_c) / keys_c;

            for (int i = startPoint.Position; i < endPoint.Position; i++)
            {
                var point = new SnapshotPoint(snapshot, i);
                //TODO: use case sensivity
                if (Char.ToLower(point.GetChar()) == Char.ToLower(_easyMotionUtil.TargetChar)
                  && navigateIndex < keys_c - groups_count) //FIXME: no depends to keys dict length.
                //SHOULD: split characters in groups
                {
                    string key = NavigationKeys[navigateIndex];
                    navigateIndex++;
                    AddNavigateToPoint(point, key);
                }
                else
                {
                    //string key = NavigationKeys.Last(); //FIXME
                    //AddNavigateToPoint(point, key);
                }
            }
            if (navigateIndex == 0)
            {
                _easyMotionUtil.ChangeToLookingCharNotFound();
            }
        }

        private void AddNavigateToPoint(IWpfTextViewLineCollection textViewLines, SnapshotPoint point, string key)
        {
            _navigateMap[key] = point;

            //_newNavigateMap[point] = key;

            var span = new SnapshotSpan(point, 1);
            var hotSpotUI = CreateHotSpotUI(point, key) ;
            if (hotSpotUI == null) return;
            _adornmentLayer.AddAdornment(span, _tag, hotSpotUI);
        }

        private TextBox CreateHotSpotUI(KeyValuePair<string, SnapshotPoint> hotspot)
        {
            return CreateHotSpotUI(hotspot.Value, hotspot.Key);
        }
        private TextBox CreateHotSpotUI(SnapshotPoint point, string key)
        {
            TextBounds bounds;
            //catch if user scroll layout so that one point going out of screen.
            try { bounds = _wpfTextView.TextViewLines.GetCharacterBounds(point); }
            catch { return null; }
            var textBox = new TextBox();
            textBox.Text = key;
            textBox.FontFamily = _classificationFormatMap.DefaultTextProperties.Typeface.FontFamily;
            textBox.Foreground = _editorFormatMap.GetProperties(EasyMotionNavigateFormatDefinition.Name).
                GetForegroundBrush(EasyMotionNavigateFormatDefinition.DefaultForegroundBrush);
            textBox.Background = _editorFormatMap.GetProperties(EasyMotionNavigateFormatDefinition.Name).
                GetBackgroundBrush(EasyMotionNavigateFormatDefinition.DefaultBackgroundBrush);

            textBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(textBox, bounds.TextTop);
            Canvas.SetLeft(textBox, bounds.Left);
            Canvas.SetZIndex(textBox, 10);
            return textBox;
        }
        public bool NavigateTo(string key)
        {

            var NavigateGroup = _navigateGroups.Where(e => e.Key == key);
            if (NavigateGroup.Count() > 1)
            {
                _adornmentLayer.RemoveAdornmentsByTag(_tag);
                foreach (var keygroup in NavigateGroup)
                {
                       //CreateHotSpotUI()
                }

                _easyMotionUtil.ChangeToLookingForChar();
            }

            SnapshotPoint point;
            if (!_navigateMap.TryGetValue(key, out point))
            {
                return false;
            }

            if (point.Snapshot != _wpfTextView.TextSnapshot)
            {
                return false;
            }

            _wpfTextView.Caret.MoveTo(point);
            return true;
        }
    }
}
