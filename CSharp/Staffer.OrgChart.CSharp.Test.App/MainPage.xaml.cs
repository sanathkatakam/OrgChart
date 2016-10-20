﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Staffer.OrgChart.Annotations;
using Staffer.OrgChart.Layout;
using Staffer.OrgChart.Misc;
using Staffer.OrgChart.Test;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Staffer.OrgChart.CSharp.Test.App
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private AutoResetEvent m_progressWaitHandle = new AutoResetEvent(false);
        private TestDataSource m_dataSource;
        private Diagram m_diagram;
        private ObservableCollection<NodeViewModel> m_nodesForTreeCollection;

        private void StartWithFullReset_Click(object sender, RoutedEventArgs e)
        {
            StartLayout(true, true);
        }

        public void StartCurrent_Click(object sender, RoutedEventArgs e)
        {
            StartLayout(false, false);
        }

        public void StartWithCleanLayout_Click(object sender, RoutedEventArgs e)
        {
            StartLayout(false, true);
        }

        private void StartLayout(bool resetBoxes, bool resetLayout)
        {
            // release any existing progress on background layout
            m_progressWaitHandle?.Dispose();
            m_progressWaitHandle = new AutoResetEvent(true);

            // re-create source data, diagram and layout data structures
            if (resetBoxes)
            {
                m_dataSource = new TestDataSource();
                new TestDataGen().GenerateDataItems(m_dataSource, 200);

                var boxContainer = new BoxContainer(m_dataSource);

                TestDataGen.GenerateBoxSizes(boxContainer);

                m_diagram = new Diagram {Boxes = boxContainer};

                m_diagram.LayoutSettings.LayoutStrategies.Add("linear",
                    new LinearLayoutStrategy {ParentAlignment = BranchParentAlignment.Center});
                m_diagram.LayoutSettings.LayoutStrategies.Add("multiline",
                    new MultiLineHangerLayoutStrategy {ParentAlignment = BranchParentAlignment.Center});
                m_diagram.LayoutSettings.DefaultLayoutStrategyId = "multiline";

                boxContainer.BoxesById[2].LayoutStrategyId = "multiline";
            }
            else if (resetLayout)
            {
                LayoutAlgorithm.ResetBoxPositions(m_diagram);
            }

            var state = new LayoutState(m_diagram);
            
            if (CbInteractiveMode.IsChecked.GetValueOrDefault(false))
            {
                state.BoundaryChanged += StateBoundaryChanged;
            }

            state.OperationChanged += StateOperationChanged;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    LayoutAlgorithm.Apply(state);
                }
                finally
                {
                    m_progressWaitHandle.Dispose();
                    m_progressWaitHandle = null;
                }
            });
        }

        private void ProgressButton_Click(object sender, RoutedEventArgs e)
        {
            m_progressWaitHandle?.Set();
        }

        private void QuickLayout()
        {
            var state = new LayoutState(m_diagram);
            state.BoxSizeFunc = dataId => m_diagram.Boxes.BoxesByDataId[dataId].Frame.Exterior.Size;

            LayoutAlgorithm.Apply(state);

            RenderBoxes(m_diagram.VisualTree, DrawCanvas);
        }

        private void BoxOnDoubleTapped(object sender, DoubleTappedRoutedEventArgs doubleTappedRoutedEventArgs)
        {
            var shape = (Rectangle) sender;
            var box = (Box) shape.DataContext;
            box.IsCollapsed = !box.IsCollapsed;
            QuickLayout();
        }

        #region Layout Event Handlers

        private void StateOperationChanged(object sender, LayoutStateOperationChangedEventArgs args)
        {
            if (args.State.CurrentOperation > LayoutState.Operation.Preparing)
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.High, () => UpdateListView(args.State.VisualTree));
            }

            if (args.State.CurrentOperation == LayoutState.Operation.Completed)
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.High, () => RenderBoxes(args.State.VisualTree, DrawCanvas));
            }
        }

        private void StateBoundaryChanged(object sender, BoundaryChangedEventArgs args)
        {
            if (args.State.CurrentOperation > LayoutState.Operation.VerticalLayout && args.State.CurrentOperation < LayoutState.Operation.Completed)
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    RenderBoxes(args.State.VisualTree, DrawCanvas);
                    RenderCurrentBoundary(args, DrawCanvas);
                });

                // wait until user releases the wait handle
                try
                {
                    m_progressWaitHandle.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    // silently exit if this wait handle is not longer valid
                    args.State.BoundaryChanged -= StateBoundaryChanged;
                    args.State.OperationChanged -= StateOperationChanged;
                }
            }
        }

        #endregion

        #region Rendering

        private void UpdateListView(Tree<int, Box, NodeLayoutInfo> visualTree)
        {
            m_nodesForTreeCollection = new ObservableCollection<NodeViewModel>(visualTree.Roots.Select(x => new NodeViewModel {Node = x}));
            LvBoxes.ItemsSource = m_nodesForTreeCollection;
        }

        private static void RenderCurrentBoundary([NotNull] BoundaryChangedEventArgs args, [NotNull]Canvas drawCanvas)
        {
            var boundary = args.Boundary;
            var top = boundary.BoundingRect.Top;
            if (top == double.MinValue)
            {
                return;
            }

            Action<List<Boundary.Step>> render = steps =>
            {
                foreach (var step in steps)
                {
                    drawCanvas.Children.Add(new Line
                    {
                        X1 = step.X,
                        Y1 = step.Top,
                        X2 = step.X,
                        Y2 = step.Bottom,
                        Stroke = new SolidColorBrush(Colors.Red),
                        StrokeThickness = 2
                    });
                }
            };

            render(boundary.Left);
            render(boundary.Right);
        }

        private void RenderBoxes(Tree<int, Box, NodeLayoutInfo> visualTree, Canvas drawCanvas)
        {
            drawCanvas.Children.Clear();

            var boundingRect = LayoutAlgorithm.ComputeBranchVisualBoundingRect(visualTree);
            drawCanvas.Width = boundingRect.Size.Width;
            drawCanvas.Height = boundingRect.Size.Height;
            drawCanvas.RenderTransform = new TranslateTransform
            {
                X = -boundingRect.Left,
                Y = -boundingRect.Top
            };

            Func<Tree<int, Box, NodeLayoutInfo>.TreeNode, bool> renderBox = node =>
            {
                if (node.Level == 0)
                {
                    return true;
                }

                var box = node.Element;
                if (!box.AffectsLayout)
                {
                    return true;
                }

                var frame = box.Frame;

                var boxRectangle = new Rectangle
                {
                    RenderTransform =
                        new TranslateTransform {X = frame.Exterior.Left, Y = frame.Exterior.Top},
                    Width = frame.Exterior.Size.Width,
                    Height = frame.Exterior.Size.Height,
                    Fill = new SolidColorBrush(box.IsSpecial ? Colors.DarkGray : box.IsCollapsed ? Colors.BurlyWood : Colors.Beige) {Opacity = box.IsSpecial ? 0.1 : 1},
                    Stroke = new SolidColorBrush(box.IsSpecial ? Colors.DarkGray : Colors.Black) { Opacity = box.IsSpecial ? 0.1 : 1 },
                    StrokeThickness = 1,
                    DataContext = box
                };

                boxRectangle.DoubleTapped += BoxOnDoubleTapped;

                drawCanvas.Children.Add(boxRectangle);

                drawCanvas.Children.Add(new TextBlock
                {
                    RenderTransform =
                        new TranslateTransform {X = frame.Exterior.Left + 5, Y = frame.Exterior.Top + 5},
                    Width = double.NaN,
                    Height = double.NaN,
                    Text = box.IsSpecial ? "" : $"{box.Id} ({box.DataId})",
                    IsHitTestVisible = false
                });

                if (!box.IsCollapsed && box.Frame.Connector != null)
                {
                    foreach (var edge in box.Frame.Connector.Segments)
                    {
                        drawCanvas.Children.Add(new Line
                        {
                            X1 = edge.From.X,
                            Y1 = edge.From.Y,
                            X2 = edge.To.X,
                            Y2 = edge.To.Y,
                            Stroke = new SolidColorBrush(Colors.Black),
                            StrokeThickness = 1,
                            IsHitTestVisible = false
                        });
                    }
                }

                return true;
            };

            visualTree.IterateChildFirst(renderBox);
        }

        #endregion
    }
}
