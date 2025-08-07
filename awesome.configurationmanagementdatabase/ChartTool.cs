using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace awesome.configurationmanagementdatabase
{
    public class ChartTool
    {
        public static void CreateChart(string filePath, string title, string xTitle, string yTitle, int width, int height, List<ChartSeries> chartSeriesList)
        {
            var myModel = new PlotModel { Title = title };

            var categoryAxis1 = new CategoryAxis {MinorStep = 1, Angle = 90};
            foreach (var items in chartSeriesList.First().ChartDataItems)
            {
                categoryAxis1.Labels.Add(items.X.ToString("MMM yy"));
            }
            myModel.Axes.Add(categoryAxis1);

            myModel.LegendPlacement = LegendPlacement.Outside;

            foreach (var chartSeries in chartSeriesList)
            {
                var series = new ColumnSeries
                {
                    Title = chartSeries.Title,
                    FillColor = OxyColor.Parse(chartSeries.HexColour),
                    StrokeThickness = chartSeries.Thickness,
                    IsStacked = true
                };
                foreach (var chartDataItem in chartSeries.ChartDataItems)
                {
                    series.Items.Add(new ColumnItem(chartDataItem.Y));
                }
                myModel.Series.Add(series);
            }

            myModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = yTitle, FontSize = 20 });
            //myModel.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, Title = xTitle, FontSize = 12, StringFormat = "MMM - yyyy", Angle = 90});

            using var stream = File.Create(filePath);
            var exporter = new SvgExporter { Width = width, Height = height };
            exporter.Export(myModel, stream);
        }

    }

    public class ChartSeries
    {
        public List<ChartData> ChartDataItems { get; set; }
        public string Title { get; set; }
        public double Thickness { get; set; }
        public string HexColour { get; set; }
    }

    public class ChartData
    {
        public DateTime X { get; set; }
        public double Y { get; set; }
    }
}
