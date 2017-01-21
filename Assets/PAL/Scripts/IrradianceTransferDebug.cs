//
// POLYGONAL AREA LIGHTS
// The MIT License (MIT)
// Copyright (c) 2016-2017 ALEXANDER PETRYAEV
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is furnished 
// to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A 
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION 
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(MeshAreaLight))]
public partial class IrradianceTransfer : MonoBehaviour
{
	/*
	void PrintPolygonMap(string name)
	{
		System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "PolygonMap_" + name + ".txt", false, System.Text.Encoding.ASCII );

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		int fieldSize = _numPolygons.ToString().Length;
		string fieldFormat = "{0," + fieldSize.ToString() + "} ";

		for( int y=bufferHeight-1; y>=0; y-- )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				if( _polygonMap[y*bufferWidth + x] >= 0 )
				{
					streamWriter.Write( string.Format( fieldFormat, _polygonMap[y*bufferWidth + x] ) );
				}
				else
				{
					streamWriter.Write( string.Format( fieldFormat, " "  ) );
				}
			}
			streamWriter.Write( "\n" );
		}

		streamWriter.Close();
	}

	void PrintThresholdMap(string name)
	{
		System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "ThresholdMap_" + name + ".txt", false, System.Text.Encoding.ASCII );

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		for( int y=bufferHeight-1; y>=0; y-- )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				if( _thresholdMap[y*bufferWidth + x] )
				{
					streamWriter.Write( "1" );
				}
				else
				{
					streamWriter.Write( "0" );
				}
			}
			streamWriter.Write( "\n" );
		}

		streamWriter.Close();
	}

	void PrintContourMap(string name)
	{
		System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "ContourMap_" + name + ".txt", false, System.Text.Encoding.ASCII );

		int bufferHeight = _depthBuffer.height-1;
		int bufferWidth = _depthBuffer.width-1;

		string[] ContourVariants = new string[]{ "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F" };

		for( int y=bufferHeight-1; y>=0; y-- )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				var contourValue = _contourMap[y*bufferWidth + x];
				if( contourValue >= 0 && contourValue < 16 )
				{
					streamWriter.Write( ContourVariants[contourValue] );
				}
				else
				{
					streamWriter.Write( " " );
				}

			}
			streamWriter.Write( "\n" );
		}

		streamWriter.Close();
	}*/
}