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
		Vector3 worldSpacePlaneNormal = Vector3.zero;

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

				viewPortSpaceVertexPos.x *= _irradianceMapInvBufferResolution.x;
				viewPortSpaceVertexPos.y *= _irradianceMapInvBufferResolution.y;

				_readWriteVertexBuffer.vertices[_numBatchVertices].flag = (uint)( _numBatchPolygons+1 );
				_readWriteVertexBuffer.vertices[_numBatchVertices].position = viewPortSpaceVertexPos;
				_readWriteVertexBuffer.vertices[_numBatchVertices].localIndex = (uint)numPolygonVertices;
				_readWriteVertexBuffer.vertices[_numBatchVertices].lastLocalIndex = 0;

				_numBatchVertices++;
				numPolygonVertices++;

				if( _numBatchVertices >= _readWriteVertexBuffer.Length )
				{
					_readWriteVertexBuffer.Resize( _readWriteVertexBuffer.Length + GPUGroupSize );
				}
			}

			irradiancePolygon.Vertices = new Vector3[numPolygonVertices];

			// complete vertex data
			for( int j=irradiancePolygon.BatchIndex; j<irradiancePolygon.BatchIndex+numPolygonVertices; j++ )
			{
				_readWriteVertexBuffer.vertices[j].lastLocalIndex = (uint)( numPolygonVertices-1 );
			}

			numPolygonVertices = 0;

			// set polygon plane
			_polygonPlanes[_numBatchPolygons].position = irradiancePolygon.pointOnPolygonPlane;
			_polygonPlanes[_numBatchPolygons].normal = irradiancePolygon.polygonPlaneNormal;
			_numBatchPolygons++;
			if( _numBatchPolygons >= _polygonPlanes.Length )
			{
				System.Array.Resize<PolygonPlane>( ref _polygonPlanes, _polygonPlanes.Length * 2 );
			}
		}
	}

	void ProcessPolygonsGPU()
	{
		// align buffer size to GPUGroupSize

		int batchBufferSize = ( _numBatchVertices / GPUGroupSize + ( ( _numBatchVertices % GPUGroupSize != 0 ) ? 1 : 0 ) ) * GPUGroupSize;
		batchBufferSize = Mathf.Max( batchBufferSize, GPUGroupSize );

		if( _readWriteVertexBuffer.Length > batchBufferSize )
		{
			_readWriteVertexBuffer.Resize( batchBufferSize );
		}

		if( _inVertexBuffer == null || ( _inVertexBuffer != null && _inVertexBuffer.count != batchBufferSize ) )
		{
			if( _inVertexBuffer != null )
			{
				_inVertexBuffer.Release();
			}
			_inVertexBuffer = new ComputeBuffer( batchBufferSize, Vertex.SizeOf );
		}

		if( _outVertexBuffer == null || ( _outVertexBuffer != null && _outVertexBuffer.count != batchBufferSize ) )
		{
			if( _outVertexBuffer != null )
			{
				_outVertexBuffer.Release();
			}
			_outVertexBuffer = new ComputeBuffer( batchBufferSize, Vertex.SizeOf );
		}

		if( _polygonPlaneBuffer == null || ( _polygonPlaneBuffer != null && _polygonPlaneBuffer.count != _polygonPlanes.Length ) )
		{
			if( _polygonPlaneBuffer != null )
			{
				_polygonPlaneBuffer.Release();
			}
			_polygonPlaneBuffer = new ComputeBuffer( _polygonPlanes.Length, PolygonPlane.SizeOf );
		}

		// set remaining vertices to be ignored by the algorithm

		for( int i=_numBatchVertices; i<_readWriteVertexBuffer.vertices.Length; i++ )
		{
			_readWriteVertexBuffer.vertices[i].flag = 0;
		}

		// set buffers

		_polygonPlaneBuffer.SetData( _polygonPlanes );
		_inVertexBuffer.SetData( _readWriteVertexBuffer.vertices );

		// ComputeShader kernels

		int projectViewportVerticesKernel = _computeShader.FindKernel( "ProjectViewportVertices" );
		if( projectViewportVerticesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find ProjectViewportVertices kernel!" );
			return;
		}

		int smoothVerticesKernel = _computeShader.FindKernel( "SmoothVertices" );
		if( smoothVerticesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find SmoothVertices kernel!" );
			return;
		}

		int reduceSemiParallelEdgesKernel = _computeShader.FindKernel( "ReduceSemiParallelEdges" );
		if( reduceSemiParallelEdgesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find ReduceSemiParallelEdges kernel!" );
			return;
		}

		int reduceSparseSemiParallelEdgesKernel = _computeShader.FindKernel( "ReduceSparseSemiParallelEdges" );
		if( reduceSparseSemiParallelEdgesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find ReduceSparseSemiParallelEdges kernel!" );
			return;
		}

		int mergeEvenVerticesKernel = _computeShader.FindKernel( "MergeEvenVertices" );
		if( mergeEvenVerticesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find MergeEvenVertices kernel!" );
			return;
		}

		int mergeSparseVerticesKernel = _computeShader.FindKernel( "MergeSparseVertices" );
		if( mergeSparseVerticesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find MergeSparseVertices kernel!" );
			return;
		}

		int removeLesserEdgesKernel = _computeShader.FindKernel( "RemoveLesserEdges" );
		if( removeLesserEdgesKernel == -1 )
		{
			Debug.LogError( "[IrradianceTransfer] SmoothAndReducePolygonsGPU() : unable to find RemoveLesserEdges kernel!" );
			return;
		}

		// project viewport vertices on to polygon planes

		Vector3 lowerLeftViewportPoint = new Vector3( 0, 0, 0 );
		Vector3 upperLeftViewportPoint = new Vector3( 0, 1, 0 );
		Vector3 lowerRightViewportPoint = new Vector3( 1, 0, 0 );
		Vector3 upperRightViewportPoint = new Vector3( 1, 1, 0 );

		Ray lowerLeftViewportRay = _offscreenCamera.ViewportPointToRay( lowerLeftViewportPoint );
		Ray upperLeftViewportRay = _offscreenCamera.ViewportPointToRay( upperLeftViewportPoint );
		Ray lowerRightViewportRay = _offscreenCamera.ViewportPointToRay( lowerRightViewportPoint );
		Ray upperRightViewportRay = _offscreenCamera.ViewportPointToRay( upperRightViewportPoint );

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

		_computeShader.SetBuffer( projectViewportVerticesKernel, "polygonPlanes", _polygonPlaneBuffer );
		_computeShader.SetBuffer( projectViewportVerticesKernel, "inBuffer", _inVertexBuffer );
		_computeShader.SetBuffer( projectViewportVerticesKernel, "outBuffer", _outVertexBuffer );
		_computeShader.Dispatch( projectViewportVerticesKernel, GPUGroupSize, 1, 1 );

		PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

		// smooth projected vertices

		int totalSmoothSteps = GPUSmoothSteps + (int)(Resolution);
		for( int smoothStep=0; smoothStep<totalSmoothSteps; smoothStep++ )
		{
			if( smoothStep > 0 )
			{
				PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );
			}
			_computeShader.SetBuffer( smoothVerticesKernel, "inBuffer", _inVertexBuffer );
			_computeShader.SetBuffer( smoothVerticesKernel, "outBuffer", _outVertexBuffer );
			_computeShader.Dispatch( smoothVerticesKernel, GPUGroupSize, 1, 1 );
		}

		// reduce semi-parallel edges, pass 0

		PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

		_computeShader.SetBuffer( reduceSemiParallelEdgesKernel, "inBuffer", _inVertexBuffer );
		_computeShader.SetBuffer( reduceSemiParallelEdgesKernel, "outBuffer", _outVertexBuffer );
		_computeShader.SetFloat( "thresholdAngleCosine", Mathf.Cos( Mathf.Deg2Rad * GPUSemiParallelEdgeAngle0 ) );
		_computeShader.Dispatch( reduceSemiParallelEdgesKernel, GPUGroupSize, 1, 1 );

		// merge even vertices

		PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

		_computeShader.SetBuffer( mergeEvenVerticesKernel, "inBuffer", _inVertexBuffer );
		_computeShader.SetBuffer( mergeEvenVerticesKernel, "outBuffer", _outVertexBuffer );
		_computeShader.SetInt( "thresholdLastLocalIndex", GPUMergeEvenVerticesThreshold );
		_computeShader.Dispatch( mergeEvenVerticesKernel, GPUGroupSize, 1, 1 );

		// reduce semi-parallel edges, pass 1

		PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

		_computeShader.SetBuffer( reduceSparseSemiParallelEdgesKernel, "inBuffer", _inVertexBuffer );
		_computeShader.SetBuffer( reduceSparseSemiParallelEdgesKernel, "outBuffer", _outVertexBuffer );
		_computeShader.SetFloat( "thresholdAngleCosine", Mathf.Cos( Mathf.Deg2Rad * GPUSemiParallelEdgeAngle1 ) );
		_computeShader.Dispatch( reduceSparseSemiParallelEdgesKernel, GPUGroupSize, 1, 1 );

		// merge sparse vertices

		PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

		_computeShader.SetBuffer( mergeSparseVerticesKernel, "inBuffer", _inVertexBuffer );
		_computeShader.SetBuffer( mergeSparseVerticesKernel, "outBuffer", _outVertexBuffer );
		_computeShader.SetInt( "thresholdLastLocalIndex", GPUMergeSparseVerticesThreshold );
		_computeShader.SetInt( "sparseness", 2 );
		_computeShader.Dispatch( mergeSparseVerticesKernel, GPUGroupSize, 1, 1 );

		// reduce semi-parallel edges, pass 2

		PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

		_computeShader.SetBuffer( reduceSparseSemiParallelEdgesKernel, "inBuffer", _inVertexBuffer );
		_computeShader.SetBuffer( reduceSparseSemiParallelEdgesKernel, "outBuffer", _outVertexBuffer );
		_computeShader.SetFloat( "thresholdAngleCosine", Mathf.Cos( Mathf.Deg2Rad * GPUSemiParallelEdgeAngle2 ) );
		_computeShader.Dispatch( reduceSparseSemiParallelEdgesKernel, GPUGroupSize, 1, 1 );

		// remove lesser edges

		for( int removeStep=0; removeStep<3; removeStep++ )
		{
			PALUtils.Swap<ComputeBuffer>( ref _inVertexBuffer, ref _outVertexBuffer );

			switch( removeStep )
			{
			case 0:
				_computeShader.SetFloat( "thresholdAngleCosine", Mathf.Cos( Mathf.Deg2Rad * GPUEdgeAngle0 ) );
				_computeShader.SetFloat( "thresholdEdgeRatio", GPUEdgeRatio0 );
				break;
			case 1:
				_computeShader.SetFloat( "thresholdAngleCosine", Mathf.Cos( Mathf.Deg2Rad * GPUEdgeAngle1 ) );
				_computeShader.SetFloat( "thresholdEdgeRatio", GPUEdgeRatio1 );
				break;
			case 2:
				_computeShader.SetFloat( "thresholdAngleCosine", Mathf.Cos( Mathf.Deg2Rad * GPUEdgeAngle2 ) );
				_computeShader.SetFloat( "thresholdEdgeRatio", GPUEdgeRatio2 );
				break;
			default:
				break;
			}

			_computeShader.SetBuffer( removeLesserEdgesKernel, "inBuffer", _inVertexBuffer );
			_computeShader.SetBuffer( removeLesserEdgesKernel, "outBuffer", _outVertexBuffer );
			_computeShader.Dispatch( removeLesserEdgesKernel, GPUGroupSize, 1, 1 );
		}

		// get data

		_outVertexBuffer.GetData( _readWriteVertexBuffer.vertices );

		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			IrradiancePolygon irradiancePolygon = _irradiancePolygons[i];
			if( irradiancePolygon == null ) continue;

			int numPolygonVertices = irradiancePolygon.Vertices.Length;
			int numReducedVertices = 0;
			int startVertex = irradiancePolygon.BatchIndex;

			for( int j=0; j<numPolygonVertices; j++ )
			{
				if( _readWriteVertexBuffer.vertices[startVertex+j].flag == 1 )
				{
					irradiancePolygon.Vertices[numReducedVertices] = _readWriteVertexBuffer.vertices[startVertex+j].position;
					numReducedVertices++;
				}
			}

			if( numReducedVertices < numPolygonVertices )
			{
				System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, numReducedVertices );
			}
		}
	}
}
