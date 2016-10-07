﻿namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// An edge in the diagram logical coordinate space.
    /// </summary>
    public struct Edge
    {
        /// <summary>
        /// Coordinates of the beginning.
        /// </summary>
        public readonly Point From;
        /// <summary>
        /// Coordinates of the end.
        /// </summary>
        public readonly Point To;

        /// <summary>
        /// Ctr.
        /// </summary>
        public Edge(Point from, Point to)
        {
            From = from;
            To = to;
        }
    }
}