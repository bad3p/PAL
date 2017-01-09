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

#define SHOW_POLYGON_PROCESSING_PASSES
#undef SHOW_POLYGON_PROCESSING_PASSES

#define SHOW_VERTEX_LABELS
#undef SHOW_VERTEX_LABELS

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(MeshAreaLight))]
public partial class IrradianceTransfer : MonoBehaviour
{
	public class ArrayBuffer<T>
	{
		public T[] buffer;

		public ArrayBuffer()
		{
			buffer = new T[0];
		}

		public ArrayBuffer(int size)
		{
			buffer = new T[size];
		}

		public void Resize(int newSize)
		{
			System.Array.Resize<T>( ref buffer, newSize );
		}

		public int Length
		{
			get { return buffer.Length; }
		}

		static public void Swap(ref ArrayBuffer<T> buffer0, ref ArrayBuffer<T> buffer1)
		{
			ArrayBuffer<T> temp = buffer0;
			buffer0 = buffer1;
			buffer1 = temp;
		}
	};

	public struct Ajacency
	{
		public uint prevIndex; // sizeof(uint) = 4
		public uint nextIndex; // sizeof(uint) = 4
		public float prevEdgeLength; // sizeof(float) = 4
		public float nextEdgeLength; // sizeof(float) = 4

		public const int SizeOf = 16; // ComputeShader stride
	};

	public struct BatchPolygon
	{
		public uint  processed;               // sizeof(uint) = 4
		public uint  startVertexIndex;        // sizeof(uint) = 4
		public uint  endVertexIndex;          // sizeof(uint) = 4
		public uint  numVertices;             // sizeof(uint) = 4
		public float polygonArea;             // sizeof(float) = 4
		public float averageTripletArea;      // sizeof(float) = 4
		public float lowerAverageTripletArea; // sizeof(float) = 4
		public float upperAverageTripletArea; // sizeof(float) = 4
		public float averageEdgeLength;       // sizeof(float) = 4
		public float lowerAverageEdgeLength;  // sizeof(float) = 4
		public float upperAverageEdgeLength;  // sizeof(float) = 4
		public uint  maxEdgeStartVertex;      // sizeof(uint) = 4
		public uint  maxEdgeEndVertex;        // sizeof(uint) = 4

		public const int SizeOf = 48; // ComputeShader stride
	};

	ArrayBuffer<Vector2> _inVertexPositions = new ArrayBuffer<Vector2>( GPUGroupSize );
	ArrayBuffer<Vector2> _outVertexPositions = new ArrayBuffer<Vector2>( GPUGroupSize );

	ArrayBuffer<uint> _inVertexFlags = new ArrayBuffer<uint>( GPUGroupSize );
	ArrayBuffer<uint> _outVertexFlags = new ArrayBuffer<uint>( GPUGroupSize );

	ArrayBuffer<uint> _inTripletFlags = new ArrayBuffer<uint>( GPUGroupSize );
	ArrayBuffer<uint> _outTripletFlags = new ArrayBuffer<uint>( GPUGroupSize );

	ArrayBuffer<Ajacency> _inVertexAjacency = new ArrayBuffer<Ajacency>( GPUGroupSize );
	ArrayBuffer<Ajacency> _outVertexAjacency = new ArrayBuffer<Ajacency>( GPUGroupSize );

	ArrayBuffer<float> _inTripletAreas = new ArrayBuffer<float>( GPUGroupSize );
	ArrayBuffer<float> _outTripletAreas = new ArrayBuffer<float>( GPUGroupSize );

	ArrayBuffer<uint> _inPolygonIndices = new ArrayBuffer<uint>( GPUGroupSize );

	ArrayBuffer<BatchPolygon> _inBatchPolygons = new ArrayBuffer<BatchPolygon>( 16 );
	ArrayBuffer<BatchPolygon> _outBatchPolygons = new ArrayBuffer<BatchPolygon>( 16 );
	ArrayBuffer<uint> _outBatchPolygonSizes = new ArrayBuffer<uint>( 16 );

	void MarchingSquaresCPU(PixelCoords marchingSquaresInf, PixelCoords marchingSquaresSup)
	{
		const byte Zero = 0;
		const byte BitMask0 = 8;
		const byte BitMask1 = 4;
		const byte BitMask2 = 2;
		const byte BitMask3 = 1;

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

		// number of vertices & polygons in all produced outlines
		_numBatchVertices = 0;
		_numBatchPolygons = 0;

		int numPolygonVertices = 0;

		for( int polygonIndex=0; polygonIndex<_irradiancePolygons.Length; polygonIndex++ )
		{
			var irradiancePolygon = _irradiancePolygons[polygonIndex];
			if( irradiancePolygon == null ) continue;

			// location of this polygon's vertices in batch buffer
			irradiancePolygon.BatchIndex = _numBatchVertices;

			// fill contour map

			int leftMostXCoord = -1;
			int leftMostContourIndex = -1;
			int numContourCells = 0;
			int contourIndex;
			int pixelIndex0;
			int pixelIndex1;
			int pixelIndex2;
			int pixelIndex3;
			bool thresholdValue0;
			bool thresholdValue1;
			bool thresholdValue2;
			bool thresholdValue3;

			for( int y=marchingSquaresInf.y; y<marchingSquaresSup.y; y++ )
			{
				int leftMostCellIndex = y*(bufferWidth-1);

				pixelIndex0 = (y+1)*bufferWidth + marchingSquaresInf.x;
				pixelIndex3 = y*bufferWidth + marchingSquaresInf.x;
				thresholdValue0 = ( _polygonMap[pixelIndex0] == polygonIndex );
				thresholdValue3 = ( _polygonMap[pixelIndex3] == polygonIndex );

				for( int x=marchingSquaresInf.x; x<marchingSquaresSup.x; x++ )
				{
					contourIndex = leftMostCellIndex+x;

					pixelIndex1 = (y+1)*bufferWidth + (x+1);
					pixelIndex2 = y*bufferWidth + (x+1);
					thresholdValue1 = ( _polygonMap[pixelIndex1] == polygonIndex );
					thresholdValue2 = ( _polygonMap[pixelIndex2] == polygonIndex );

					byte lookupIndex = 0;
					lookupIndex |= ( thresholdValue0 ? BitMask0 : Zero );
					lookupIndex |= ( thresholdValue1 ? BitMask1 : Zero );
					lookupIndex |= ( thresholdValue2 ? BitMask2 : Zero );
					lookupIndex |= ( thresholdValue3 ? BitMask3 : Zero );

					_contourMap[contourIndex] = lookupIndex;

					if( lookupIndex > 0 && lookupIndex < 15 )
					{
						numContourCells += ( lookupIndex == 5 || lookupIndex == 10 ) ? 2 : 1;
						if( leftMostContourIndex == -1 || ( leftMostContourIndex != -1 && leftMostXCoord > x ) )
						{
							leftMostContourIndex = contourIndex;
							leftMostXCoord = x;
						}
					}

					pixelIndex0 = pixelIndex1;
					pixelIndex3 = pixelIndex2;
					thresholdValue0 = thresholdValue1;
					thresholdValue3 = thresholdValue2;
				}
			}

			if( leftMostContourIndex < 0 )
			{
				Debug.LogError( "[IrradianceTransfer] leftMostContourIndex == " + leftMostContourIndex + "!" );
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
			Vector3 w = Vector3.zero;
			Vector3 worldSpaceVertexPos = Vector3.zero;
			Vector3 worldSpaceVertexOffset = Vector3.zero;
			float edgeLength;

			// choose better point on polygon plane
			{
				viewPortSpaceVertexPos.x = irradiancePolygon.marchingSquaresInf.x + ( irradiancePolygon.marchingSquaresSup.x - irradiancePolygon.marchingSquaresInf.x ) / 2;
				viewPortSpaceVertexPos.y = irradiancePolygon.marchingSquaresInf.y + ( irradiancePolygon.marchingSquaresSup.y - irradiancePolygon.marchingSquaresInf.y ) / 2;
				viewPortSpaceVertexPos.x *= _irradianceMapInvBufferResolution.x;
				viewPortSpaceVertexPos.y *= _irradianceMapInvBufferResolution.y;

				Ray ray = _offscreenCamera.ViewportPointToRay( viewPortSpaceVertexPos );
				Vector3 rayOrigin = ray.origin;
				Vector3 rayDirection = ray.direction;

				float distance = float.MaxValue;
				w = rayOrigin - irradiancePolygon.pointOnPolygonPlane;
				distance = -Vector3.Dot( irradiancePolygon.polygonPlaneNormal, w ) / Vector3.Dot( irradiancePolygon.polygonPlaneNormal, rayDirection );

				irradiancePolygon.pointOnPolygonPlane = ( rayOrigin + rayDirection * distance );
			}

			while( move != Stop )
			{
				viewPortSpaceVertexPos.x = currentContourIndex % (bufferWidth-1) + 0.5f;
				viewPortSpaceVertexPos.y = currentContourIndex / (bufferWidth-1) + 0.5f;

				byte currentPolygonContourValue = _contourMap[currentContourIndex];

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

				Ray ray = _offscreenCamera.ViewportPointToRay( viewPortSpaceVertexPos );
				Vector3 rayOrigin = ray.origin;
				Vector3 rayDirection = ray.direction;

				// distance from point on near clip plane to polygon plane
				float distance = float.MaxValue;
				w = rayOrigin - irradiancePolygon.pointOnPolygonPlane;
				distance = -Vector3.Dot( irradiancePolygon.polygonPlaneNormal, w ) / Vector3.Dot( irradiancePolygon.polygonPlaneNormal, rayDirection );

				worldSpaceVertexPos = ( rayOrigin + rayDirection * distance );
				worldSpaceVertexOffset = worldSpaceVertexPos - irradiancePolygon.pointOnPolygonPlane;

				_inVertexPositions.buffer[_numBatchVertices].Set( Vector3.Dot( worldSpaceVertexOffset, irradiancePolygon.polygonPlaneTangent ), Vector3.Dot( worldSpaceVertexOffset, irradiancePolygon.polygonPlaneBitangent ) );
				_inVertexFlags.buffer[_numBatchVertices] = 1;
				_inTripletFlags.buffer[_numBatchVertices] = 1;
				_inVertexAjacency.buffer[_numBatchVertices].prevIndex = (uint)( ( numPolygonVertices > 0 ) ? ( _numBatchVertices - 1 ) : ( _numBatchVertices ) );
				_inVertexAjacency.buffer[_numBatchVertices].nextIndex = (uint)( _numBatchVertices + 1 );
				_inTripletAreas.buffer[_numBatchVertices] = 0.0f;
				_inPolygonIndices.buffer[_numBatchVertices] = (uint)( _numBatchPolygons );

				edgeLength = Vector2.Distance( _inVertexPositions.buffer[_numBatchVertices], _inVertexPositions.buffer[_inVertexAjacency.buffer[_numBatchVertices].prevIndex] );
				_inVertexAjacency.buffer[_numBatchVertices].prevEdgeLength = edgeLength ;
				_inVertexAjacency.buffer[_inVertexAjacency.buffer[_numBatchVertices].prevIndex].nextEdgeLength = edgeLength;

				_numBatchVertices++;
				numPolygonVertices++;

				if( _numBatchVertices >= _inVertexPositions.Length )
				{
					int newSize = _numBatchVertices + GPUGroupSize;
					_inVertexPositions.Resize( newSize );
					_inVertexFlags.Resize( newSize );
					_inTripletFlags.Resize( newSize );
					_inVertexAjacency.Resize( newSize );
					_inTripletAreas.Resize( newSize );
					_inPolygonIndices.Resize( newSize );
				}
			}

			irradiancePolygon.Vertices = new Vector3[numPolygonVertices];

			// loop ajacency

			_inVertexAjacency.buffer[irradiancePolygon.BatchIndex].prevIndex = (uint)( _numBatchVertices-1 );
			_inVertexAjacency.buffer[_numBatchVertices-1].nextIndex = (uint)( irradiancePolygon.BatchIndex );

			edgeLength = Vector2.Distance( _inVertexPositions.buffer[irradiancePolygon.BatchIndex], _inVertexPositions.buffer[_numBatchVertices-1] );
			_inVertexAjacency.buffer[irradiancePolygon.BatchIndex].prevEdgeLength = edgeLength;
			_inVertexAjacency.buffer[_numBatchVertices-1].nextEdgeLength = edgeLength;

			// fill batch polygon

			_inBatchPolygons.buffer[_numBatchPolygons].processed = 0;
			_inBatchPolygons.buffer[_numBatchPolygons].startVertexIndex = (uint)( irradiancePolygon.BatchIndex );
			_inBatchPolygons.buffer[_numBatchPolygons].endVertexIndex = (uint)( _numBatchVertices-1 );
			_inBatchPolygons.buffer[_numBatchPolygons].numVertices = (uint)( numPolygonVertices );
			_inBatchPolygons.buffer[_numBatchPolygons].polygonArea = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].averageTripletArea = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].lowerAverageTripletArea = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].upperAverageTripletArea = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].averageEdgeLength = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].lowerAverageEdgeLength = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].upperAverageEdgeLength = 0.0f;
			_inBatchPolygons.buffer[_numBatchPolygons].maxEdgeStartVertex = uint.MaxValue;
			_inBatchPolygons.buffer[_numBatchPolygons].maxEdgeEndVertex = uint.MaxValue;

			_numBatchPolygons++;

			numPolygonVertices = 0;

			if( _numBatchPolygons >= _inBatchPolygons.Length )
			{
				_inBatchPolygons.Resize( _inBatchPolygons.Length * 2 );
			}
		}

		if( _outVertexPositions.Length != _inVertexPositions.Length )
		{
			int newSize = _inVertexPositions.Length;
			_outVertexPositions.Resize( newSize );
			_outVertexFlags.Resize( newSize );
			_outTripletFlags.Resize( newSize );
			_outVertexAjacency.Resize( newSize );
			_outTripletAreas.Resize( newSize );
		}

		if( _outBatchPolygons.Length != _inBatchPolygons.Length )
		{
			_outBatchPolygons.Resize( _inBatchPolygons.Length );
		}

		if( _outBatchPolygonSizes.Length != _inBatchPolygons.Length )
		{
			_outBatchPolygonSizes.Resize( _inBatchPolygons.Length );
		}

		ProcessPolygonsCPU();
	}

	#if SHOW_POLYGON_PROCESSING_PASSES
	int _polygonProcessingPass = 0;
	#endif

	#if SHOW_VERTEX_LABELS
	List<KeyValuePair<Vector3,string>> _vertexLabels = new List<KeyValuePair<Vector3,string>>();
	#endif

	#if SHOW_POLYGON_PROCESSING_PASSES
	List<KeyValuePair<Vector3,Color>> _tripletColors = new List<KeyValuePair<Vector3,Color>>();
	#endif

	void DrawVertexLabels()
	{
		#if SHOW_VERTEX_LABELS
		{
			foreach( var vertexLabel in _vertexLabels )
			{
				UnityEditor.Handles.Label( vertexLabel.Key, vertexLabel.Value );
			}
		}
		#endif

		#if SHOW_POLYGON_PROCESSING_PASSES
		{			
			foreach( var tripletColor in _tripletColors )
			{
				Gizmos.color = tripletColor.Value;
				Gizmos.DrawSphere( tripletColor.Key, 0.0125f );
			}

			for( int i=0; i<_irradiancePolygons.Length; i++ )
			{
				Gizmos.color = Color.green;
				var irradiancePolygon = _irradiancePolygons[i];
				if( irradiancePolygon != null )
				{
					Gizmos.DrawSphere( irradiancePolygon.pointOnPolygonPlane, 0.025f );
				}
			}
		}
		#endif
	}

	void ProcessPolygonsCPU()
	{
		uint prevNumTotalVertices = (uint)_numBatchVertices;

		InitializeTriplets( ref _inVertexPositions.buffer, ref _inVertexAjacency.buffer, ref _outTripletAreas.buffer, _numBatchVertices );
		ArrayBuffer<float>.Swap( ref _inTripletAreas, ref _outTripletAreas );

		UpdateBatchPolygons( ref _inVertexPositions.buffer, ref _inTripletAreas.buffer, ref _inTripletFlags.buffer, ref _inVertexAjacency.buffer, ref _inVertexFlags.buffer, ref _inBatchPolygons.buffer, ref _outBatchPolygons.buffer, ref _outBatchPolygonSizes.buffer, 0, _numBatchPolygons );
		ArrayBuffer<BatchPolygon>.Swap( ref _inBatchPolygons, ref _outBatchPolygons );

		const int MaxSimplificationSteps = 128;

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 1 )
		#endif
		{
			for( int simplificationStep=0; simplificationStep<MaxSimplificationSteps; simplificationStep++ )
			{
				SimplifyPolygons( ref _inPolygonIndices.buffer, ref _inBatchPolygons.buffer, ref _inVertexFlags.buffer, ref _inVertexAjacency.buffer, ref _inTripletAreas.buffer, ref _inTripletFlags.buffer, ref _outVertexFlags.buffer, ref _outTripletFlags.buffer, 1, _numBatchVertices );
				ArrayBuffer<uint>.Swap( ref _inVertexFlags, ref _outVertexFlags );

				// do not involve triplet flags in first step
				if( simplificationStep > 1 )
				{
					ArrayBuffer<uint>.Swap( ref _inTripletFlags, ref _outTripletFlags );
				}

				/*if( simplificationStep <= 2 )
				{
					SmoothVertices( ref _inVertexPositions.buffer, ref _inVertexAjacency.buffer, ref _inPolygonIndices.buffer, ref _inBatchPolygons.buffer, ref _outVertexPositions.buffer, _numBatchVertices );
					ArrayBuffer<Vector2>.Swap( ref _inVertexPositions, ref _outVertexPositions );
				}*/

				UpdateAjacencyAndTriplets( ref _inVertexPositions.buffer, ref _inVertexAjacency.buffer, ref _inVertexFlags.buffer, ref _inTripletAreas.buffer, ref _inTripletFlags.buffer, ref _inPolygonIndices.buffer, ref _inBatchPolygons.buffer, ref _outVertexAjacency.buffer, ref _outTripletAreas.buffer, ref _outTripletFlags.buffer, _numBatchVertices );
				ArrayBuffer<Ajacency>.Swap( ref _inVertexAjacency, ref _outVertexAjacency );
				ArrayBuffer<float>.Swap( ref _inTripletAreas, ref _outTripletAreas );
				ArrayBuffer<uint>.Swap( ref _inTripletFlags, ref _outTripletFlags );

				UpdateBatchPolygons( ref _inVertexPositions.buffer, ref _inTripletAreas.buffer, ref _inTripletFlags.buffer, ref _inVertexAjacency.buffer, ref _inVertexFlags.buffer, ref _inBatchPolygons.buffer, ref _outBatchPolygons.buffer, ref _outBatchPolygonSizes.buffer, simplificationStep+1, _numBatchPolygons );
				ArrayBuffer<BatchPolygon>.Swap( ref _inBatchPolygons, ref _outBatchPolygons );

				#if SHOW_POLYGON_PROCESSING_PASSES
				if( 1 + simplificationStep > _polygonProcessingPass )
				{
					break;
				}
				#endif

				#if !SHOW_POLYGON_PROCESSING_PASSES
				{
					uint totalVertices = 0;
					for( uint i=0; i<_numBatchPolygons; i++ )
					{
						totalVertices += _outBatchPolygonSizes.buffer[i];
					}
					if( totalVertices < prevNumTotalVertices )
					{
						prevNumTotalVertices = totalVertices;
					}
					else
					{
						break;
					}
				}
				#endif
			}
		}

		#if SHOW_POLYGON_PROCESSING_PASSES
			_polygonProcessingPass++;
			if( _polygonProcessingPass >= 1 + MaxSimplificationSteps ) _polygonProcessingPass = 0;
		#endif

		// get data 

		ArrayBuffer<Vector2>.Swap( ref _inVertexPositions, ref _outVertexPositions );
		ArrayBuffer<uint>.Swap( ref _inVertexFlags, ref _outVertexFlags );
		ArrayBuffer<uint>.Swap( ref _inTripletFlags, ref _outTripletFlags );
		ArrayBuffer<Ajacency>.Swap( ref _inVertexAjacency, ref _outVertexAjacency );
		ArrayBuffer<float>.Swap( ref _inTripletAreas, ref _outTripletAreas );

		#if SHOW_VERTEX_LABELS
			_vertexLabels.Clear();
		#endif

		#if SHOW_POLYGON_PROCESSING_PASSES
			_tripletColors.Clear();
		#endif

		int batchPolygonIndex = 0;
		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			IrradiancePolygon irradiancePolygon = _irradiancePolygons[i];
			if( irradiancePolygon == null ) continue;

			int numPolygonVertices = irradiancePolygon.Vertices.Length;
			int numReducedVertices = 0;
			int startVertex = irradiancePolygon.BatchIndex;

			#if SHOW_VERTEX_LABELS
			Vector3 polygonLabelPosition = Vector3.zero;
			#endif

			for( int j=0; j<numPolygonVertices; j++ )
			{
				if( _outVertexFlags.buffer[startVertex+j] == 1 )
				{
					irradiancePolygon.Vertices[numReducedVertices] = 
					(
						irradiancePolygon.pointOnPolygonPlane +
						_outVertexPositions.buffer[startVertex+j].x * irradiancePolygon.polygonPlaneTangent +
						_outVertexPositions.buffer[startVertex+j].y * irradiancePolygon.polygonPlaneBitangent
					);

					#if SHOW_VERTEX_LABELS
					{						
						string label = j.ToString();
						_vertexLabels.Add( new KeyValuePair<Vector3,string>( irradiancePolygon.Vertices[numReducedVertices], label ) );
						polygonLabelPosition += irradiancePolygon.Vertices[numReducedVertices];
					}
					#endif

					#if SHOW_POLYGON_PROCESSING_PASSES
					{
						if( _outTripletFlags.buffer[startVertex+j] == 0 )
						{
							_tripletColors.Add( new KeyValuePair<Vector3,Color>( irradiancePolygon.Vertices[numReducedVertices], Color.red ) );
						}
					}
					#endif

					numReducedVertices++;
				}
			}

			#if SHOW_VERTEX_LABELS
			{
				polygonLabelPosition *= 1.0f / numReducedVertices;
				string label = batchPolygonIndex.ToString() + " : " + numReducedVertices.ToString() + "\n" + _inBatchPolygons.buffer[batchPolygonIndex].polygonArea.ToString( "F5" ) + "\n" + _inBatchPolygons.buffer[batchPolygonIndex].lowerAverageTripletArea.ToString( "F5" ) + " / " + _inBatchPolygons.buffer[batchPolygonIndex].averageTripletArea.ToString( "F5" ) + " / " + _inBatchPolygons.buffer[batchPolygonIndex].upperAverageTripletArea.ToString( "F5" );
				_vertexLabels.Add( new KeyValuePair<Vector3,string>( polygonLabelPosition, label ) );
			}
			#endif

			if( numReducedVertices < numPolygonVertices )
			{
				System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, numReducedVertices );
			}

			batchPolygonIndex++;
		}
	}

	static void SmoothVertices(ref Vector2[] inVertexPositionBuffer, ref Ajacency[] inAjacencyBuffer, ref uint[] inPolygonIndexBuffer, ref BatchPolygon[] inBatchPolygonBuffer, ref Vector2[] outVertexPositionBuffer, int numBatchVertices)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			uint prevIndex = inAjacencyBuffer[index].prevIndex;
			uint nextIndex = inAjacencyBuffer[index].nextIndex;
			outVertexPositionBuffer[index] = ( inVertexPositionBuffer[prevIndex]  + inVertexPositionBuffer[index] + inVertexPositionBuffer[nextIndex] ) / 3;
		}
	}

	static float TriangleArea(Vector2 v1, Vector2 v2, Vector2 v3)
	{
		float a = Vector2.Distance( v1, v2 );
		float b = Vector2.Distance( v2, v3 );
		float c = Vector2.Distance( v3, v1 );
		float sum = (a+b+c)*(b+c-a)*(c+a-b)*(a+b-c);
		return 0.25f * Mathf.Sqrt( ( sum > 0 ) ? ( sum ) : ( 0 ) );
	}

	static void InitializeTriplets(ref Vector2[] inVertexPositionBuffer, ref Ajacency[] inAjacencyBuffer, ref float[] outTripletAreaBuffer, int numBatchVertices)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			uint prevIndex = inAjacencyBuffer[index].prevIndex;
			uint nextIndex = inAjacencyBuffer[index].nextIndex;
			outTripletAreaBuffer[index] = TriangleArea( inVertexPositionBuffer[prevIndex], inVertexPositionBuffer[index], inVertexPositionBuffer[nextIndex] );
		}
	}

	static void SimplifyPolygons(ref uint[] inPolygonIndexBuffer, ref BatchPolygon[] inBatchPolygonBuffer, ref uint[] inVertexFlagBuffer, ref Ajacency[] inVertexAjacencyBuffer, ref float[] inTripletAreaBuffer, ref uint[] inTripletFlagBuffer, ref uint[] outVertexFlagBuffer, ref uint[] outTripletFlagBuffer, int maxAjacencyDistance, int numBatchVertices)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			uint batchPolygonIndex = inPolygonIndexBuffer[index];

			if( inBatchPolygonBuffer[batchPolygonIndex].processed == 1 )
			{
				outVertexFlagBuffer[index] = inVertexFlagBuffer[index];
				outTripletFlagBuffer[index] = inTripletFlagBuffer[index];
				continue;
			}

			if( inVertexFlagBuffer[index] == 1 && inTripletFlagBuffer[index] == 1 )
			{
				float polygonArea = inBatchPolygonBuffer[batchPolygonIndex].polygonArea;
				float lowerAverageTripletArea = inBatchPolygonBuffer[batchPolygonIndex].lowerAverageTripletArea;
				float upperAverageTripletArea = inBatchPolygonBuffer[batchPolygonIndex].upperAverageTripletArea;
				float upperAverageEdgeLength = inBatchPolygonBuffer[batchPolygonIndex].upperAverageEdgeLength;
				uint numPolygonVertices = inBatchPolygonBuffer[batchPolygonIndex].numVertices;
				uint maxEdgeStartVertex = inBatchPolygonBuffer[batchPolygonIndex].maxEdgeStartVertex;
				uint maxEdgeEndVertex = inBatchPolygonBuffer[batchPolygonIndex].maxEdgeEndVertex;

				//float maxReducibleTripletArea = 0.33f * polygonArea / numPolygonVertices; // TODO: move to Update()
				float maxReducibleTripletArea = 0.01f * polygonArea;

				float tripletArea = inTripletAreaBuffer[index];

				if( tripletArea <= maxReducibleTripletArea && tripletArea <= lowerAverageTripletArea )
				{
					uint prevIndex = inVertexAjacencyBuffer[index].prevIndex;
					uint nextIndex = inVertexAjacencyBuffer[index].nextIndex;

					bool isSimplifiable = true;
					for( uint ajacencyDistance=0; ajacencyDistance<(uint)(maxAjacencyDistance); ajacencyDistance++ )
					{
						if( inTripletFlagBuffer[prevIndex] == 1 )
						{
							float prevTripletArea = inTripletAreaBuffer[prevIndex];
							if( tripletArea > prevTripletArea )
							{
								isSimplifiable = false;
								break;
							}
						}

						if( inTripletFlagBuffer[nextIndex] == 1 )
						{
							float nextTripletArea = inTripletAreaBuffer[nextIndex];
							if( tripletArea > nextTripletArea )
							{
								isSimplifiable = false;
								break;
							}
						}

						prevIndex = inVertexAjacencyBuffer[prevIndex].prevIndex;
						nextIndex = inVertexAjacencyBuffer[nextIndex].nextIndex;
					}

					if( isSimplifiable )
					{
						outVertexFlagBuffer[index] = 0;
						outTripletFlagBuffer[index] = 0;
					}
					else
					{
						outVertexFlagBuffer[index] = 1;
						outTripletFlagBuffer[index] = 1;
					}
				}
				else if( index == maxEdgeStartVertex || index == maxEdgeEndVertex )
				{
					outVertexFlagBuffer[index] = 1;
					outTripletFlagBuffer[index] = 0;
				}
				else 
				{					
					outVertexFlagBuffer[index] = 1;
					outTripletFlagBuffer[index] = 1;
				}
			}
			else if( inVertexFlagBuffer[index] == 1 && inTripletFlagBuffer[index] == 0 )
			{
				float upperAverageEdgeLength = inBatchPolygonBuffer[batchPolygonIndex].upperAverageEdgeLength;

				if( inVertexAjacencyBuffer[index].prevEdgeLength < upperAverageEdgeLength &&
					inVertexAjacencyBuffer[index].nextEdgeLength < upperAverageEdgeLength )
				{
					outVertexFlagBuffer[index] = 1;
					outTripletFlagBuffer[index] = 1;
				}
				else
				{
					outVertexFlagBuffer[index] = 1;
					outTripletFlagBuffer[index] = 0;
				}
			}
			else
			{
				outVertexFlagBuffer[index] = 0;
				outTripletFlagBuffer[index] = 0;
			}
		}
	}

	static void UpdateAjacencyAndTriplets(ref Vector2[] inVertexPositionBuffer, ref Ajacency[] inAjacencyBuffer, ref uint[] inFlagBuffer, ref float[] inTripletAreaBuffer, ref uint[] inTripletFlagBuffer, ref uint[] inPolygonIndexBuffer, ref BatchPolygon[] inBatchPolygonBuffer, ref Ajacency[] outAjacencyBuffer, ref float[] outTripletAreaBuffer, ref uint[] outTripletFlagBuffer, int numBatchVertices)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			uint batchPolygonIndex = inPolygonIndexBuffer[index];

			if( inBatchPolygonBuffer[batchPolygonIndex].processed == 1 )
			{
				outAjacencyBuffer[index].nextEdgeLength = inAjacencyBuffer[index].nextEdgeLength;
				outAjacencyBuffer[index].nextIndex = inAjacencyBuffer[index].nextIndex;
				outAjacencyBuffer[index].prevEdgeLength = inAjacencyBuffer[index].prevEdgeLength;
				outAjacencyBuffer[index].prevIndex = inAjacencyBuffer[index].prevIndex;
				outTripletAreaBuffer[index] = inTripletAreaBuffer[index];
				outTripletFlagBuffer[index] = inTripletFlagBuffer[index];
			}
			else
			{
				if( inFlagBuffer[index] == 1 )
				{
					uint prevIndex = inAjacencyBuffer[index].prevIndex;
					uint nextIndex = inAjacencyBuffer[index].nextIndex;

					bool isTripletAreaOutOfDate = ( inFlagBuffer[prevIndex] == 0 ) || ( inFlagBuffer[nextIndex] == 0 );

					if( isTripletAreaOutOfDate )
					{
						while( inFlagBuffer[prevIndex] != 1 && prevIndex != index ) 
						{
							prevIndex = inAjacencyBuffer[prevIndex].prevIndex;
						}

						while( inFlagBuffer[nextIndex] != 1 && nextIndex != index ) 
						{
							nextIndex = inAjacencyBuffer[nextIndex].nextIndex;
						}

						outAjacencyBuffer[index].prevIndex = prevIndex;
						outAjacencyBuffer[index].nextIndex = nextIndex;
						outAjacencyBuffer[index].prevEdgeLength = Vector2.Distance( inVertexPositionBuffer[prevIndex], inVertexPositionBuffer[index] );
						outAjacencyBuffer[index].nextEdgeLength = Vector2.Distance( inVertexPositionBuffer[nextIndex], inVertexPositionBuffer[index] );
						outTripletAreaBuffer[index] = TriangleArea( inVertexPositionBuffer[prevIndex], inVertexPositionBuffer[index], inVertexPositionBuffer[nextIndex] );
						outTripletFlagBuffer[index] = inTripletFlagBuffer[index];
					}
					else
					{
						outAjacencyBuffer[index].prevIndex = inAjacencyBuffer[index].prevIndex;
						outAjacencyBuffer[index].nextIndex = inAjacencyBuffer[index].nextIndex;
						outAjacencyBuffer[index].prevEdgeLength = inAjacencyBuffer[index].prevEdgeLength;
						outAjacencyBuffer[index].nextEdgeLength = inAjacencyBuffer[index].nextEdgeLength;
						outTripletAreaBuffer[index] = inTripletAreaBuffer[index];
						outTripletFlagBuffer[index] = inTripletFlagBuffer[index];
					}
				}
				else
				{
					outAjacencyBuffer[index].prevIndex = inAjacencyBuffer[index].prevIndex;
					outAjacencyBuffer[index].nextIndex = inAjacencyBuffer[index].nextIndex;
					outAjacencyBuffer[index].prevEdgeLength = inAjacencyBuffer[index].prevEdgeLength;
					outAjacencyBuffer[index].nextEdgeLength = inAjacencyBuffer[index].nextEdgeLength;
					outTripletAreaBuffer[index] = inTripletAreaBuffer[index];
					outTripletFlagBuffer[index] = inTripletFlagBuffer[index];
				}
			}
		}
	}

	static void UpdateBatchPolygons(ref Vector2[] inVertexPositionBuffer, ref float[] inTripletAreaBuffer, ref uint[] inTripletFlagBuffer, ref Ajacency[] inAjacencyBuffer, ref uint[] inFlagBuffer, ref BatchPolygon[] inBatchPolygonBuffer, ref BatchPolygon[] outBatchPolygonBuffer, ref uint[] outBatchPolygonSizeBuffer, int updateStep, int numBatchPolygons)
	{
		for( uint polygonIndex=0; polygonIndex<numBatchPolygons; polygonIndex++ )
		{
			if( inBatchPolygonBuffer[polygonIndex].processed == 1 )
			{
				continue;
			}

			uint startVertexIndex = 0;
			for( uint vertexIndex=inBatchPolygonBuffer[polygonIndex].startVertexIndex; vertexIndex<=inBatchPolygonBuffer[polygonIndex].endVertexIndex; vertexIndex++ )
			{
				if( inFlagBuffer[vertexIndex] == 1 )
				{
					startVertexIndex = vertexIndex;
					break;
				}
			}

			if( startVertexIndex == inBatchPolygonBuffer[polygonIndex].endVertexIndex )
			{
				outBatchPolygonBuffer[polygonIndex].processed = 1;
				outBatchPolygonBuffer[polygonIndex].startVertexIndex = startVertexIndex;
				outBatchPolygonBuffer[polygonIndex].endVertexIndex = startVertexIndex;
				outBatchPolygonBuffer[polygonIndex].numVertices = inFlagBuffer[startVertexIndex];
				outBatchPolygonBuffer[polygonIndex].polygonArea = 0.0f;
				outBatchPolygonBuffer[polygonIndex].averageTripletArea = 0.0f;
				outBatchPolygonBuffer[polygonIndex].lowerAverageTripletArea = 0.0f;
				outBatchPolygonBuffer[polygonIndex].upperAverageTripletArea = 0.0f;
				outBatchPolygonBuffer[polygonIndex].averageEdgeLength = 0.0f;
				outBatchPolygonBuffer[polygonIndex].lowerAverageEdgeLength = 0.0f;
				outBatchPolygonBuffer[polygonIndex].upperAverageEdgeLength = 0.0f;
				outBatchPolygonBuffer[polygonIndex].maxEdgeStartVertex = uint.MaxValue;
				outBatchPolygonBuffer[polygonIndex].maxEdgeEndVertex = uint.MaxValue;
				outBatchPolygonSizeBuffer[polygonIndex] = 1;
			}
			else
			{
				uint numVertices = 1;
				uint endVertexIndex = startVertexIndex;
				while( inAjacencyBuffer[endVertexIndex].nextIndex != startVertexIndex )
				{
					numVertices++;
					endVertexIndex = inAjacencyBuffer[endVertexIndex].nextIndex;
				}
				outBatchPolygonBuffer[polygonIndex].startVertexIndex = startVertexIndex;
				outBatchPolygonBuffer[polygonIndex].endVertexIndex = endVertexIndex;
				outBatchPolygonBuffer[polygonIndex].numVertices = numVertices;

				float polygonArea = 0.0f;
				float averageTripletArea = 0.0f;
				float averageEdgeLength = 0.0f;
				float maxEdgeLength = 0.0f;
				uint maxEdgeStartVertex = uint.MaxValue;
				uint maxEdgeEndVertex = uint.MaxValue;
				uint vertexIndex = startVertexIndex;
				uint numUnlockedTriplets = 0;
				Vector2 v0 = inVertexPositionBuffer[vertexIndex];
				Vector2 v1 = Vector2.zero;
				while( vertexIndex != endVertexIndex )
				{
					vertexIndex = inAjacencyBuffer[vertexIndex].nextIndex;
					v1 = inVertexPositionBuffer[vertexIndex];
					polygonArea += v0.x*v1.y - v1.x*v0.y;
					if( inTripletFlagBuffer[vertexIndex] == 1 )
					{
						averageTripletArea += inTripletAreaBuffer[vertexIndex];
						numUnlockedTriplets++;
					}
					averageEdgeLength += inAjacencyBuffer[vertexIndex].nextEdgeLength;
					if( inTripletFlagBuffer[vertexIndex] == 1 || inTripletFlagBuffer[inAjacencyBuffer[vertexIndex].nextIndex] == 1 )
					{
						if( maxEdgeLength < inAjacencyBuffer[vertexIndex].nextEdgeLength )
						{
							maxEdgeLength = inAjacencyBuffer[vertexIndex].nextEdgeLength;
							maxEdgeStartVertex = vertexIndex;
							maxEdgeEndVertex = inAjacencyBuffer[vertexIndex].nextIndex;
						}
					}
					v0 = v1;
				}
				v1 = inVertexPositionBuffer[startVertexIndex];
				polygonArea += v0.x*v1.y - v1.x*v0.y;

				if( inTripletFlagBuffer[endVertexIndex] == 1 )
				{
					averageTripletArea += inTripletAreaBuffer[endVertexIndex];
					numUnlockedTriplets++;
				}

				if( numUnlockedTriplets > 0 )
				{
					averageTripletArea = averageTripletArea / numUnlockedTriplets;
				}

				averageEdgeLength += inAjacencyBuffer[endVertexIndex].nextEdgeLength;
				averageEdgeLength = averageEdgeLength / numVertices;

				if( inTripletFlagBuffer[endVertexIndex] == 1 || inTripletFlagBuffer[inAjacencyBuffer[endVertexIndex].nextIndex] == 1 )
				{
					if( maxEdgeLength < inAjacencyBuffer[endVertexIndex].nextEdgeLength )
					{
						maxEdgeLength = inAjacencyBuffer[endVertexIndex].nextEdgeLength;
						maxEdgeStartVertex = endVertexIndex;
						maxEdgeEndVertex = inAjacencyBuffer[endVertexIndex].nextIndex;
					}
				}

				float lowerAverageTripletArea = 0.0f;
				float upperAverageTripletArea = 0.0f;
				if( averageTripletArea > 0 )
				{
					uint numLowerAverageTriplets = 0;
					uint numUpperAverageTriplets = 0;

					vertexIndex = startVertexIndex;
					while( vertexIndex != endVertexIndex )
					{
						if( inTripletFlagBuffer[vertexIndex] == 1 )
						{
							if( inTripletAreaBuffer[vertexIndex] < averageTripletArea )
							{
								lowerAverageTripletArea += inTripletAreaBuffer[vertexIndex];
								numLowerAverageTriplets++;
							}
							else
							{
								upperAverageTripletArea += inTripletAreaBuffer[vertexIndex];
								numUpperAverageTriplets++;
							}
						}

						vertexIndex = inAjacencyBuffer[vertexIndex].nextIndex;
					}

					if( inTripletFlagBuffer[endVertexIndex] == 1 )
					{
						if( inTripletAreaBuffer[endVertexIndex] < averageTripletArea )
						{
							lowerAverageTripletArea += inTripletAreaBuffer[endVertexIndex];
							numLowerAverageTriplets++;
						}
						else
						{
							upperAverageTripletArea += inTripletAreaBuffer[endVertexIndex];
							numUpperAverageTriplets++;
						}
					}

					if( numLowerAverageTriplets > 0 )
					{
						lowerAverageTripletArea = lowerAverageTripletArea / numLowerAverageTriplets;
					}
					else
					{
						lowerAverageTripletArea = 0.0f;
					}

					if( numUpperAverageTriplets > 0 )
					{
						upperAverageTripletArea = upperAverageTripletArea / numUpperAverageTriplets;
					}
					else
					{
						upperAverageTripletArea = 0.0f;
					}
				}

				float lowerAverageEdgeLength = 0.0f;
				float upperAverageEdgeLength = 0.0f;
				if( averageEdgeLength > 0 )
				{
					uint numLowerAverageEdges = 0;
					uint numUpperAverageEdges = 0;

					vertexIndex = startVertexIndex;
					while( vertexIndex != endVertexIndex )
					{
						if( inAjacencyBuffer[vertexIndex].nextEdgeLength < averageEdgeLength )
						{
							lowerAverageEdgeLength += inAjacencyBuffer[vertexIndex].nextEdgeLength;
							numLowerAverageEdges++;
						}
						else
						{
							upperAverageEdgeLength += inAjacencyBuffer[vertexIndex].nextEdgeLength;
							numUpperAverageEdges++;
						}

						vertexIndex = inAjacencyBuffer[vertexIndex].nextIndex;
					}

					if( inAjacencyBuffer[endVertexIndex].nextEdgeLength < averageEdgeLength )
					{
						lowerAverageEdgeLength += inAjacencyBuffer[endVertexIndex].nextEdgeLength;
						numLowerAverageEdges++;
					}
					else
					{
						upperAverageEdgeLength += inAjacencyBuffer[endVertexIndex].nextEdgeLength;
						numUpperAverageEdges++;
					}

					if( numLowerAverageEdges > 0 )
					{
						lowerAverageEdgeLength = lowerAverageEdgeLength / numLowerAverageEdges;
					}
					else
					{
						lowerAverageEdgeLength = 0.0f;
					}

					if( numUpperAverageEdges > 0 )
					{
						upperAverageEdgeLength = upperAverageEdgeLength / numUpperAverageEdges;
					}
					else
					{
						upperAverageEdgeLength = 0.0f;
					}
				}

				if( maxEdgeLength < upperAverageEdgeLength )
				{
					maxEdgeLength = 0.0f;
				}

				if( updateStep == 0 )
				{
					outBatchPolygonBuffer[polygonIndex].processed = 0;
				}
				else
				{
					if( numVertices == inBatchPolygonBuffer[polygonIndex].numVertices )
					{
						outBatchPolygonBuffer[polygonIndex].processed = 1;
					}
					else
					{
						outBatchPolygonBuffer[polygonIndex].processed = 0;
					}
				}
				outBatchPolygonBuffer[polygonIndex].numVertices = numVertices;
				outBatchPolygonBuffer[polygonIndex].polygonArea = -0.5f * polygonArea;
				outBatchPolygonBuffer[polygonIndex].averageTripletArea = averageTripletArea;
				outBatchPolygonBuffer[polygonIndex].lowerAverageTripletArea = lowerAverageTripletArea;
				outBatchPolygonBuffer[polygonIndex].upperAverageTripletArea = upperAverageTripletArea;
				outBatchPolygonBuffer[polygonIndex].averageEdgeLength = averageEdgeLength;
				outBatchPolygonBuffer[polygonIndex].lowerAverageEdgeLength = lowerAverageEdgeLength;
				outBatchPolygonBuffer[polygonIndex].upperAverageEdgeLength = upperAverageEdgeLength;
				outBatchPolygonBuffer[polygonIndex].maxEdgeStartVertex = maxEdgeStartVertex;
				outBatchPolygonBuffer[polygonIndex].maxEdgeEndVertex = maxEdgeEndVertex;
				outBatchPolygonSizeBuffer[polygonIndex] = numVertices;
			}
		}
	}
}