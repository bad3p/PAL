//
// POLYGONAL AREA LIGHTS
// The MIT License (MIT)
// Copyright (c) 2016 ALEXANDER PETRYAEV
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
	void MarchingSquaresGPU(PixelCoords marchingSquaresInf, PixelCoords marchingSquaresSup)
	{
		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		// set polygons on map borders to -1
		for( int i=0; i<bufferWidth; i++ )
		{
			_polygonMap[i] = -1;
			_polygonMap[(bufferWidth)*(bufferHeight-1)+i] = -1;
		}
		for( int i=0; i<bufferHeight; i++ )
		{
			_polygonMap[bufferWidth*i] = -1;
			_polygonMap[bufferWidth*i+bufferWidth-1] = -1;
		}

		_polygonMapBuffer.SetData( _polygonMap );

		int marchingSquaresKernel = _computeShader.FindKernel( "MarchingSquares" );

		if( marchingSquaresKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] MarchingSquaresGPU() : unable to find MarchingSquares kernel!" );
			return;
		}

		int numPolygonIndices = 0;
		int[] polygonIndices = new int[8];
		Vector4 polygonIndices0123 = Vector4.zero;
		Vector4 polygonIndices4567 = Vector4.zero;

		// number of vertices & polygons in all produced outlines
		_numBatchVertices = 0;
		_numBatchPolygons = 0;

		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			var irradiancePolygon = _irradiancePolygons[i];
			if( ( irradiancePolygon == null ) && ( i < _irradiancePolygons.Length-1 ) ) 
			{
				continue;
			}
			else
			{
				// workaround for bugged ComputeShader.SetInts
				switch( numPolygonIndices )
				{
				case 0: polygonIndices0123.x = i; break;
				case 1: polygonIndices0123.y = i; break;
				case 2: polygonIndices0123.z = i; break;
				case 3: polygonIndices0123.w = i; break;
				case 4: polygonIndices4567.x = i; break;
				case 5: polygonIndices4567.y = i; break;
				case 6: polygonIndices4567.z = i; break;
				case 7: polygonIndices4567.w = i; break;
				default: break;
				}
				polygonIndices[numPolygonIndices] = i;
				numPolygonIndices++;
			}

			if( numPolygonIndices == 8 || i == ( _irradiancePolygons.Length-1 ) )
			{
				_computeShader.SetInt( "numPolygons", numPolygonIndices );
				_computeShader.SetVector( "polygonIndices0123", polygonIndices0123 );
				_computeShader.SetVector( "polygonIndices4567", polygonIndices4567 );
				_computeShader.SetInt( "bufferWidth", bufferWidth );
				_computeShader.SetInt( "bufferHeight", bufferHeight );
				_computeShader.SetBuffer( marchingSquaresKernel, "polygonMap", _polygonMapBuffer );
				_computeShader.SetBuffer( marchingSquaresKernel, "contourMap", _contourBuffer );

				var numberOfGroups = Mathf.CeilToInt( (float) (bufferWidth-1)*(bufferHeight-1)/GPUGroupSize );
				_computeShader.Dispatch( marchingSquaresKernel, numberOfGroups, 1, 1 );

				_contourBuffer.GetData( _packedContourMap );
				BuildPolygonOutlines( numPolygonIndices, ref polygonIndices, marchingSquaresInf, marchingSquaresSup );
				numPolygonIndices = 0;
			}
		}

		ProcessPolygonsGPU();
	}

	void BuildPolygonOutlines(int numPolygonIndices, ref int[] polygonIndices, PixelCoords marchingSquaresInf, PixelCoords marchingSquaresSup)
	{
		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		int numPolygonVertices = 0;

		for( int i=0; i<numPolygonIndices; i++ )
		{
			IrradiancePolygon irradiancePolygon = _irradiancePolygons[polygonIndices[i]];
			if( irradiancePolygon == null ) continue;

			// location of this polygon's vertices in batch buffer
			irradiancePolygon.BatchIndex = _numBatchVertices;

			uint polygonContourMask = ContourMask[i];
			int polygonContourShift = i*4;

			// locate leftmost cell containing outline element of the polygon

			int leftMostContourIndex = -1;

			int xMin = irradiancePolygon.marchingSquaresInf.x > 0 ? irradiancePolygon.marchingSquaresInf.x-1 : irradiancePolygon.marchingSquaresInf.x;
			int xMax = irradiancePolygon.marchingSquaresSup.x < (bufferWidth-1) ? irradiancePolygon.marchingSquaresSup.x+1 : irradiancePolygon.marchingSquaresSup.x;
			int yMin = irradiancePolygon.marchingSquaresInf.y > 0 ? irradiancePolygon.marchingSquaresInf.y-1 : irradiancePolygon.marchingSquaresInf.y;
			int yMax = irradiancePolygon.marchingSquaresSup.y < (bufferHeight-1) ? irradiancePolygon.marchingSquaresSup.y+1 : irradiancePolygon.marchingSquaresSup.y;

			for( int x=xMin; x<xMax; x++ )
			{
				for( int y=yMin; y<yMax; y++ )
				{
					int contourIndex = y*(bufferWidth-1)+x;
					uint contourPackedValue = _packedContourMap[contourIndex];
					uint polygonContourValue = contourPackedValue & polygonContourMask;
					if( polygonContourValue != 0 )
					{
						leftMostContourIndex = y*(bufferWidth-1) + x;
						break;
					}
				}
				if( leftMostContourIndex != -1 )
				{
					break;
				}
			}

			if( leftMostContourIndex < 0 )
			{
				continue;
			}

			// transform outline to world space

			const int Start = 0;
			const int Left = 1;
			const int Up = 2;
			const int Right = 3;
			const int Down = 4;
			const int Stop = -1;

			int currentContourIndex = leftMostContourIndex;
			int move = Start;
			Vector3 viewPortSpaceVertexPos = Vector3.zero;

			uint[] prevPolygonContourValue = new uint[2] { 0, 0 };

			while( move != Stop )
			{
				viewPortSpaceVertexPos.x = currentContourIndex % (bufferWidth-1) + 0.5f;
				viewPortSpaceVertexPos.y = currentContourIndex / (bufferWidth-1) + 0.5f;

				uint currentPackedContourValue = _packedContourMap[currentContourIndex];
				uint currentPolygonContourValue = ( currentPackedContourValue & polygonContourMask ) >> polygonContourShift;

				switch( currentPolygonContourValue )
				{
				case 1:
					viewPortSpaceVertexPos.x += OutlineOffset;
					move = ( move == Right ) ? Down : Stop;
					break;
				case 2:
					viewPortSpaceVertexPos.x += 0.5f;
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Up || move == Start ) ? Right : Stop;
					break;
				case 3:
					viewPortSpaceVertexPos.x += 0.5f;
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Right ) ? Right : Stop;
					break;
				case 4:
					viewPortSpaceVertexPos.x += 0.5f - OutlineOffset;
					viewPortSpaceVertexPos.y += 0.5f;
					move = ( move == Left || move == Start ) ? Up : Stop;
					break;
				case 5:
					viewPortSpaceVertexPos.x += ( move == Right ) ? OutlineOffset : ( 0.5f - OutlineOffset );
					viewPortSpaceVertexPos.y += ( move == Right ) ? 0.5f : 0;
					move = ( move == Right ) ? Up : ( ( move == Left ) ? Down : Stop );
					break;
				case 6:
					viewPortSpaceVertexPos.x += 0.5f - OutlineOffset;
					viewPortSpaceVertexPos.y += 0.5f;
					move = ( move == Up || move == Start ) ? Up : Stop;
					break;
				case 7:
					viewPortSpaceVertexPos.x += OutlineOffset;
					viewPortSpaceVertexPos.y += 0.5f;
					move = ( move == Right ) ? Up : Stop;
					break;
				case 8:
					viewPortSpaceVertexPos.y += 0.5f - OutlineOffset;
					move = ( move == Down ) ? Left : Stop;
					break;
				case 9:
					viewPortSpaceVertexPos.x += OutlineOffset;
					move = ( move == Down ) ? Down : Stop;
					break;
				case 10:
					viewPortSpaceVertexPos.x += ( move == Down ) ? 0.5f : 0;
					viewPortSpaceVertexPos.y += ( move == Down ) ? ( 0.5f - OutlineOffset ) : OutlineOffset;
					move = ( move == Down ) ? Right : ( ( move == Up ) ? Left : Stop );
					break;
				case 11:
					viewPortSpaceVertexPos.x += 0.5f;
					viewPortSpaceVertexPos.y += 0.5f - OutlineOffset;
					move = ( move == Down ) ? Right : Stop;
					break;
				case 12:
					viewPortSpaceVertexPos.y += 0.5f - OutlineOffset;
					move = ( move == Left ) ? Left : Stop;
					break;
				case 13:
					viewPortSpaceVertexPos.x += 0.5f - OutlineOffset;
					move = ( move == Left ) ? Down : Stop;
					break;
				case 14:
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Up ) ? Left : Stop;
					break;
				default:
					move = Stop;
					break;
				};

				switch( move )
				{
				case Left:
					currentContourIndex = ( currentContourIndex % ( bufferWidth-1 ) > 0 ) ? currentContourIndex-1 : -1;
					break;
				case Up:
					currentContourIndex = ( currentContourIndex / ( bufferWidth-1 ) < ( bufferHeight-2 ) ) ? currentContourIndex+bufferWidth-1 : -1;
					break;
				case Right:
					currentContourIndex = ( currentContourIndex % ( bufferWidth-1 ) < ( bufferWidth-2 ) ) ? currentContourIndex+1 : -1;
					break;
				case Down:
					currentContourIndex = ( currentContourIndex / ( bufferWidth-1 ) > 0 ) ? currentContourIndex-bufferWidth+1 : -1;
					break;
				default:
					move = Stop;
					break;
				}

				if( currentContourIndex == leftMostContourIndex || currentContourIndex < 0 )
				{
					move = Stop;
				}

				switch( currentPolygonContourValue )
				{
				case 3:
				case 6:
				case 9:
				case 12:
					if( currentPolygonContourValue == prevPolygonContourValue[1] && currentPolygonContourValue == prevPolygonContourValue[0] ) 
					{
						_numBatchVertices--;
						numPolygonVertices--;
					}
					break;
				default:
					break;
				}

				viewPortSpaceVertexPos.x *= _irradianceMapInvBufferResolution.x;
				viewPortSpaceVertexPos.y *= _irradianceMapInvBufferResolution.y;

				_batchVertices.buffer[_numBatchVertices].flag = 0;
				_batchVertices.buffer[_numBatchVertices].prevIndex = (uint)(_numBatchVertices) - 1;
				_batchVertices.buffer[_numBatchVertices].nextIndex = (uint)(_numBatchVertices) + 1;
				_batchVertices.buffer[_numBatchVertices].position = viewPortSpaceVertexPos;
				_batchVertices.buffer[_numBatchVertices].edge.Set( 0,0 );
				_batchVertices.buffer[_numBatchVertices].edgeLength = 0.0f;
				_batchVertices.buffer[_numBatchVertices].tripletArea = 0.0f;

				_numBatchVertices++;
				numPolygonVertices++;

				if( _numBatchVertices >= _batchVertices.Length )
				{
					_batchVertices.Resize( _batchVertices.Length + GPUGroupSize );
				}

				prevPolygonContourValue[0] = prevPolygonContourValue[1];
				prevPolygonContourValue[1] = currentPolygonContourValue;
			}

			// loop ajacency data

			uint startVertexIndex = (uint)( irradiancePolygon.BatchIndex );
			uint endVertexIndex = (uint)( _numBatchVertices-1 );

			_batchVertices.buffer[startVertexIndex].prevIndex = endVertexIndex;
			_batchVertices.buffer[endVertexIndex].nextIndex = startVertexIndex;

			// fill batch polygon

			_batchPolygons.buffer[_numBatchPolygons].startVertexIndex = startVertexIndex;
			_batchPolygons.buffer[_numBatchPolygons].endVertexIndex = endVertexIndex;
			_batchPolygons.buffer[_numBatchPolygons].numVertices = (uint)( numPolygonVertices );
			_batchPolygons.buffer[_numBatchPolygons].planePosition = irradiancePolygon.pointOnPolygonPlane;
			_batchPolygons.buffer[_numBatchPolygons].planeNormal = irradiancePolygon.polygonPlaneNormal;
			_batchPolygons.buffer[_numBatchPolygons].planeTangent = irradiancePolygon.polygonPlaneTangent;
			_batchPolygons.buffer[_numBatchPolygons].planeBitangent = irradiancePolygon.polygonPlaneBitangent;

			irradiancePolygon.BatchIndex = _numBatchPolygons;

			numPolygonVertices = 0;
			_numBatchPolygons++;

			if( _numBatchPolygons >= _batchPolygons.Length )
			{
				_batchPolygons.Resize( _batchPolygons.Length + GPUGroupSize );
			}
		}
	}

	void ProcessPolygonsGPU()
	{
		if( _batchVertices.Length % GPUGroupSize != 0 )
		{
			Debug.LogError( "[IrradianceTransfer] ProcessPolygonsGPU() : ( _inBatchVertices.Length % GPUGroupSize != 0 )" );
			return;
		}

		System.Func<ComputeBuffer,int,int,ComputeBuffer> ResizeComputeBuffer = (computeBuffer, bufferSize, bufferStride) =>
		{
			if( computeBuffer == null || ( computeBuffer != null && computeBuffer.count != bufferSize ) )
			{
				if( computeBuffer != null )
				{
					computeBuffer.Release();
				}
				computeBuffer = new ComputeBuffer( bufferSize, bufferStride );
			}
			return computeBuffer;
		};

		_batchVertexBuffer = ResizeComputeBuffer( _batchVertexBuffer, _batchVertices.Length, BatchVertex.SizeOf );
		_batchPolygonBuffer = ResizeComputeBuffer( _batchPolygonBuffer, _batchPolygons.Length, BatchPolygon.SizeOf );

		_batchVertexBuffer.SetData( _batchVertices.buffer );
		_batchPolygonBuffer.SetData( _batchPolygons.buffer );

		// ComputeShader kernel

		int processPolygonsKernel = _computeShader.FindKernel( "ProcessPolygons" );
		if( processPolygonsKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] ProcessPolygonsGPU() : ( processPolygonsKernel == -1 )" );
			return;
		}

		Vector3 lowerLeftViewportPoint = new Vector3( 0, 0, 0 );
		Vector3 upperLeftViewportPoint = new Vector3( 0, 1, 0 );
		Vector3 lowerRightViewportPoint = new Vector3( 1, 0, 0 );
		Vector3 upperRightViewportPoint = new Vector3( 1, 1, 0 );

		Ray lowerLeftViewportRay = _offscreenCamera.ViewportPointToRay( lowerLeftViewportPoint );
		Ray upperLeftViewportRay = _offscreenCamera.ViewportPointToRay( upperLeftViewportPoint );
		Ray lowerRightViewportRay = _offscreenCamera.ViewportPointToRay( lowerRightViewportPoint );
		Ray upperRightViewportRay = _offscreenCamera.ViewportPointToRay( upperRightViewportPoint );

		_computeShader.SetInt( "numBatchVertices", _numBatchVertices );
		_computeShader.SetInt( "numBatchPolygons", _numBatchPolygons );
		_computeShader.SetFloat( "redundantEdgeCosineAngle", Mathf.Cos( 3.33333f * Mathf.Deg2Rad ) );
		_computeShader.SetInt( "minVertices", 5 );

		_computeShader.SetVector( "lowerLeftViewportPoint", lowerLeftViewportPoint );
		_computeShader.SetVector( "upperLeftViewportPoint", upperLeftViewportPoint );
		_computeShader.SetVector( "lowerRightViewportPoint", lowerRightViewportPoint );
		_computeShader.SetVector( "upperRightViewportPoint", upperRightViewportPoint );
		_computeShader.SetVector( "lowerLeftRayOrigin", lowerLeftViewportRay.origin );
		_computeShader.SetVector( "lowerLeftRayDirection", lowerLeftViewportRay.direction );
		_computeShader.SetVector( "upperLeftRayOrigin", upperLeftViewportRay.origin );
		_computeShader.SetVector( "upperLeftRayDirection", upperLeftViewportRay.direction );
		_computeShader.SetVector( "lowerRightRayOrigin", lowerRightViewportRay.origin );
		_computeShader.SetVector( "lowerRightRayDirection", lowerRightViewportRay.direction );
		_computeShader.SetVector( "upperRightRayOrigin", upperRightViewportRay.origin );
		_computeShader.SetVector( "upperRightRayDirection", upperRightViewportRay.direction );

		_computeShader.SetBuffer( processPolygonsKernel, "batchVertexBuffer", _batchVertexBuffer );
		_computeShader.SetBuffer( processPolygonsKernel, "batchPolygonBuffer", _batchPolygonBuffer );
		_computeShader.Dispatch( processPolygonsKernel, GPUGroupSize, 1, 1 );
		_batchVertexBuffer.GetData( _batchVertices.buffer );
		_batchPolygonBuffer.GetData( _batchPolygons.buffer );

		int batchPolygonIndex = 0;
		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			IrradiancePolygon irradiancePolygon = _irradiancePolygons[i];
			if( irradiancePolygon == null ) continue;

			int vertexIndex = (int)( _batchPolygons.buffer[batchPolygonIndex].startVertexIndex );
			int numPolygonVertices = (int)( _batchPolygons.buffer[batchPolygonIndex].numVertices );
			irradiancePolygon.Vertices = new Vector3[numPolygonVertices];

			for( int j=0; j<numPolygonVertices; j++ )
			{
				irradiancePolygon.Vertices[j] = (
					irradiancePolygon.pointOnPolygonPlane +					
					_batchVertices.buffer[vertexIndex].position.x * irradiancePolygon.polygonPlaneTangent +
					_batchVertices.buffer[vertexIndex].position.y * irradiancePolygon.polygonPlaneBitangent
				);
				vertexIndex = (int)( _batchVertices.buffer[vertexIndex].nextIndex );
			}

			batchPolygonIndex++;
		}

	}
}
