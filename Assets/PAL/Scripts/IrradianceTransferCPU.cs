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

#if UNITY_EDITOR
	#define SHOW_POLYGON_PROCESSING_PASSES
	#undef SHOW_POLYGON_PROCESSING_PASSES
	#define SHOW_VERTEX_LABELS
	#undef SHOW_VERTEX_LABELS
#endif

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(MeshAreaLight))]
public partial class IrradianceTransfer : MonoBehaviour
{
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

			// a) set "pointOnPolygonPlane" to center of polygon's bounding box in viewport space
			// b) and project it on to plane of the polygon
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

			byte[] prevPolygonContourValue = new byte[2] { 0, 0 };

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

				Ray ray = _offscreenCamera.ViewportPointToRay( viewPortSpaceVertexPos );
				Vector3 rayOrigin = ray.origin;
				Vector3 rayDirection = ray.direction;

				// distance from point on near clip plane to polygon plane
				float distance = float.MaxValue;
				w = rayOrigin - irradiancePolygon.pointOnPolygonPlane;
				distance = -Vector3.Dot( irradiancePolygon.polygonPlaneNormal, w ) / Vector3.Dot( irradiancePolygon.polygonPlaneNormal, rayDirection );

				worldSpaceVertexPos = ( rayOrigin + rayDirection * distance );
				worldSpaceVertexOffset = worldSpaceVertexPos - irradiancePolygon.pointOnPolygonPlane;

				_batchVertices.buffer[_numBatchVertices].flag = 0;
				_batchVertices.buffer[_numBatchVertices].prevIndex = (uint)(_numBatchVertices) - 1;
				_batchVertices.buffer[_numBatchVertices].nextIndex = (uint)(_numBatchVertices) + 1;
				_batchVertices.buffer[_numBatchVertices].position.Set( Vector3.Dot( worldSpaceVertexOffset, irradiancePolygon.polygonPlaneTangent ), Vector3.Dot( worldSpaceVertexOffset, irradiancePolygon.polygonPlaneBitangent ) );
				_batchVertices.buffer[_numBatchVertices].edge.Set( 0,0 );
				_batchVertices.buffer[_numBatchVertices].edgeLength = 0.0f;
				_batchVertices.buffer[_numBatchVertices].tripletArea = 0.0f;

				_numBatchVertices++;
				numPolygonVertices++;

				if( _numBatchVertices >= _batchVertices.Length )
				{
					_batchVertices.Resize( _numBatchVertices + GPUGroupSize );
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

			_numBatchPolygons++;

			numPolygonVertices = 0;

			if( _numBatchPolygons >= _batchPolygons.Length )
			{
				_batchPolygons.Resize( _batchPolygons.Length + GPUGroupSize );
			}
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
		const int MaxPolygonProcessingPasses = 128;
		const int MinPolygonsInSecondaryAreaLight = 5;

		#if SHOW_POLYGON_PROCESSING_PASSES
		{
			ProcessPolygons( ref _batchVertices.buffer, ref _batchPolygons.buffer, (uint)_numBatchVertices, (uint)_numBatchPolygons, (uint)(MinPolygonsInSecondaryAreaLight), (uint)(_polygonProcessingPass) );
			_polygonProcessingPass++;
			if( _polygonProcessingPass >= MaxPolygonProcessingPasses ) _polygonProcessingPass = 0;
		}
		#else
		{			
			ProcessPolygons( ref _batchVertices.buffer, ref _batchPolygons.buffer, (uint)_numBatchVertices, (uint)_numBatchPolygons, (uint)(MinPolygonsInSecondaryAreaLight), (uint)MaxPolygonProcessingPasses );
		}
		#endif

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

			int vertexIndex = (int)( _batchPolygons.buffer[batchPolygonIndex].startVertexIndex );
			int numPolygonVertices = (int)( _batchPolygons.buffer[batchPolygonIndex].numVertices );
			irradiancePolygon.Vertices = new Vector3[numPolygonVertices];

			for( int j=0; j<numPolygonVertices; j++ )
			{
				irradiancePolygon.Vertices[j] = 
				(
					irradiancePolygon.pointOnPolygonPlane +					
						_batchVertices.buffer[vertexIndex].position.x * irradiancePolygon.polygonPlaneTangent +
						_batchVertices.buffer[vertexIndex].position.y * irradiancePolygon.polygonPlaneBitangent
				);

				#if SHOW_VERTEX_LABELS
				{						
					string label = vertexIndex.ToString();
					label += "\n" + _batchVertices.buffer[vertexIndex].edgeLength.ToString("F4");
					label += "\n" + _batchVertices.buffer[vertexIndex].tripletArea.ToString("F4");
					_vertexLabels.Add( new KeyValuePair<Vector3,string>( irradiancePolygon.Vertices[j], label ) );
				}
				#endif

				vertexIndex = (int)( _batchVertices.buffer[vertexIndex].nextIndex );
			}

			batchPolygonIndex++;
		}
	}

	static float TripletArea(Vector2 v1, Vector2 v2, Vector2 v3)
	{
		return Mathf.Abs( 0.5f * ( -v2.x*v1.y + v3.x*v1.y + v1.x*v2.y - v3.x*v2.y - v1.x*v3.y + v2.x*v3.y ) );
	}

	static float ClockwiseAngle (Vector2 v1, Vector2 v2)
	{
		float result = Mathf.Atan2( v1.x*v2.y - v1.y*v2.x, v1.x*v2.x + v1.y*v2.y );
		if( result > 0 )
		{
			result = -( Mathf.PI * 2 - result );
		}
		return -result;
	}

	static void RemoveVertex(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex, uint vertexIndex, bool updateEdge, bool updateTriplets)
	{
		if( batchPolygonBuffer[polygonIndex].startVertexIndex > vertexIndex )
		{
			Debug.LogAssertion( "[IrradianceTransferCPU] RemoveVertex() : ( batchPolygonBuffer[polygonIndex].startVertexIndex > vertexIndex )" );
			return;
		}

		if( batchPolygonBuffer[polygonIndex].endVertexIndex < vertexIndex )
		{
			Debug.LogAssertion( "[IrradianceTransferCPU] RemoveVertex() : ( batchPolygonBuffer[polygonIndex].endVertexIndex < vertexIndex )" );
			return;
		}

		if( batchPolygonBuffer[polygonIndex].numVertices == 0 )
		{
			Debug.LogAssertion( "[IrradianceTransferCPU] RemoveVertex() : ( batchPolygonBuffer[polygonIndex].numVertices == 0 )" );
			return;
		}

		// update ajacency

		uint prevIndex = batchVertexBuffer[vertexIndex].prevIndex;
		uint nextIndex = batchVertexBuffer[vertexIndex].nextIndex;

		batchVertexBuffer[prevIndex].nextIndex = nextIndex;
		batchVertexBuffer[nextIndex].prevIndex = prevIndex;

		// update edge

		if( updateEdge )
		{
			Vector2 prevEdge = ( batchVertexBuffer[nextIndex].position - batchVertexBuffer[prevIndex].position );
			float prevEdgeLength = prevEdge.magnitude;
			prevEdge *= 1.0f / prevEdge.magnitude;

			batchVertexBuffer[prevIndex].edge = prevEdge;
			batchVertexBuffer[prevIndex].edgeLength = prevEdgeLength;
		}

		// update triplets

		if( updateTriplets )
		{
			batchVertexBuffer[prevIndex].tripletArea = TripletArea( 
				batchVertexBuffer[batchVertexBuffer[prevIndex].prevIndex].position,
				batchVertexBuffer[prevIndex].position,
				batchVertexBuffer[nextIndex].position
			);

			batchVertexBuffer[nextIndex].tripletArea = TripletArea( 
				batchVertexBuffer[prevIndex].position,
				batchVertexBuffer[nextIndex].position,
				batchVertexBuffer[batchVertexBuffer[nextIndex].nextIndex].position
			);
		}

		// update polygon

		if( vertexIndex == batchPolygonBuffer[polygonIndex].startVertexIndex )
		{
			batchPolygonBuffer[polygonIndex].startVertexIndex = nextIndex;
		}
		else if( vertexIndex == batchPolygonBuffer[polygonIndex].endVertexIndex )
		{
			batchPolygonBuffer[polygonIndex].endVertexIndex = prevIndex;
		}

		batchPolygonBuffer[polygonIndex].numVertices--;
	}

	static void InitializeEdges(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex)
	{
		uint currIndex;
		uint nextIndex;

		currIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;
		nextIndex = batchVertexBuffer[currIndex].nextIndex;

		do
		{
			Vector2 edge = ( batchVertexBuffer[nextIndex].position - batchVertexBuffer[currIndex].position );
			float edgeLength = edge.magnitude;
			edge *= 1.0f / edgeLength;

			batchVertexBuffer[currIndex].edge = edge;
			batchVertexBuffer[currIndex].edgeLength = edgeLength;

			currIndex = nextIndex;
			nextIndex = batchVertexBuffer[currIndex].nextIndex;
		}
		while( currIndex != batchPolygonBuffer[polygonIndex].startVertexIndex );
	}

	static void InitializeTriplets(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex)
	{
		uint prevIndex;
		uint currIndex;
		uint nextIndex;

		currIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;
		prevIndex = batchVertexBuffer[currIndex].prevIndex;
		nextIndex = batchVertexBuffer[currIndex].nextIndex;

		do
		{
			batchVertexBuffer[currIndex].tripletArea = TripletArea( 
				batchVertexBuffer[prevIndex].position, 
				batchVertexBuffer[currIndex].position, 
				batchVertexBuffer[nextIndex].position
			);

			prevIndex = currIndex;
			currIndex = nextIndex;
			nextIndex = batchVertexBuffer[currIndex].nextIndex;
		}
		while( currIndex != batchPolygonBuffer[polygonIndex].startVertexIndex );
	}

	static void RemoveRedundantEdges(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex, float edgeAngleCosine)
	{
		uint currIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;
		uint prevIndex = batchVertexBuffer[currIndex].prevIndex;
		uint nextIndex = batchVertexBuffer[currIndex].nextIndex;

		do
		{
			Vector2 prevEdge = batchVertexBuffer[prevIndex].edge;
			Vector2 nextEdge = batchVertexBuffer[currIndex].edge;

			if( Vector2.Dot( prevEdge, nextEdge ) > edgeAngleCosine )
			{
				RemoveVertex( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, currIndex, true, false );

				currIndex = nextIndex;
				prevIndex = batchVertexBuffer[currIndex].prevIndex;
				nextIndex = batchVertexBuffer[currIndex].nextIndex;
			}
			else
			{
				prevIndex = currIndex;
				currIndex = nextIndex;
				nextIndex = batchVertexBuffer[currIndex].nextIndex;
			}
		}
		while( currIndex != batchPolygonBuffer[polygonIndex].startVertexIndex );
	}

	static void RemoveLocalConcavity(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex, uint concavityType)
	{
		uint currIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;
		uint prevIndex = batchVertexBuffer[currIndex].prevIndex;
		uint nextIndex = batchVertexBuffer[currIndex].nextIndex;
		uint precIndex = batchVertexBuffer[prevIndex].prevIndex;

		do
		{
			Vector2 precEdge = -batchVertexBuffer[precIndex].edge; //( batchVertexBuffer[precIndex].position - batchVertexBuffer[prevIndex].position );
			Vector2 prevEdge = batchVertexBuffer[prevIndex].edge; //( batchVertexBuffer[currIndex].position - batchVertexBuffer[prevIndex].position );
			Vector2 virtualEdge = ( batchVertexBuffer[nextIndex].position - batchVertexBuffer[prevIndex].position );

			float virtualEdgeLength = virtualEdge.magnitude;
			virtualEdge *= 1.0f / virtualEdgeLength;

			// cavity
			if( ( concavityType == 0 ) && ( ClockwiseAngle( precEdge, prevEdge ) > ClockwiseAngle( precEdge, virtualEdge ) ) )
			{				
				batchVertexBuffer[currIndex].flag = 1;
			}
			// ledge
			else if( ( concavityType == 1 ) && ( ClockwiseAngle( precEdge, prevEdge ) < ClockwiseAngle( precEdge, virtualEdge ) ) )
			{
				batchVertexBuffer[currIndex].flag = 1;
			}

			precIndex = prevIndex;
			prevIndex = currIndex;
			currIndex = nextIndex;
			nextIndex = batchVertexBuffer[currIndex].nextIndex;
		}
		while( currIndex != batchPolygonBuffer[polygonIndex].startVertexIndex );

		currIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;
		do
		{
			if( batchVertexBuffer[currIndex].flag == 1 )
			{
				uint indexToRemove = currIndex;
				currIndex = batchVertexBuffer[currIndex].nextIndex;
				RemoveVertex( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, indexToRemove, true, false );
			}
			else
			{
				currIndex = batchVertexBuffer[currIndex].nextIndex;
			}
		}
		while( currIndex != batchPolygonBuffer[polygonIndex].startVertexIndex );
	}

	static void GetPolygonTripletStatistics(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex, out float averageTripletArea, out float lowerAverageTripletArea, out float upperAverageTripletArea)
	{
		averageTripletArea = 0;
		lowerAverageTripletArea = 0;
		upperAverageTripletArea = 0;

		uint startVertexIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;

		uint vertexIndex = startVertexIndex;
		do
		{
			averageTripletArea += batchVertexBuffer[vertexIndex].tripletArea;
			vertexIndex = batchVertexBuffer[vertexIndex].nextIndex;
		}
		while( vertexIndex != startVertexIndex );

		if( batchPolygonBuffer[polygonIndex].numVertices > 0 )
		{
			averageTripletArea /= batchPolygonBuffer[polygonIndex].numVertices;
		}

		uint numLowerAverageTriplets = 0;
		uint numUpperAverageTriplets = 0;
		vertexIndex = startVertexIndex;
		do
		{
			float tripletArea = batchVertexBuffer[vertexIndex].tripletArea;

			if( tripletArea < averageTripletArea )
			{
				lowerAverageTripletArea += tripletArea;
				numLowerAverageTriplets++;
			}
			else
			{
				upperAverageTripletArea += tripletArea;
				numUpperAverageTriplets++;
			}
			vertexIndex = batchVertexBuffer[vertexIndex].nextIndex;
		}
		while( vertexIndex != startVertexIndex );

		if( numLowerAverageTriplets > 0 )
		{
			lowerAverageTripletArea /= numLowerAverageTriplets;
		}

		if( numUpperAverageTriplets > 0 )
		{
			upperAverageTripletArea /= numUpperAverageTriplets;
		}
	}

	static void RemoveLesserTriplets(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex, float tripletAreaThreshold)
	{
		uint currIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;
		uint nextIndex = batchVertexBuffer[currIndex].nextIndex;

		do
		{
			float tripletArea = batchVertexBuffer[currIndex].tripletArea;

			if( tripletArea < tripletAreaThreshold )
			{
				RemoveVertex( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, currIndex, true, true );

				currIndex = nextIndex;
				nextIndex = batchVertexBuffer[currIndex].nextIndex;
			}
			else
			{
				currIndex = nextIndex;
				nextIndex = batchVertexBuffer[currIndex].nextIndex;
			}
		}
		while( currIndex != batchPolygonBuffer[polygonIndex].startVertexIndex );
	}

	static float GetPolygonArea(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint polygonIndex)
	{
		float polygonArea = 0.0f;

		uint startVertexIndex = batchPolygonBuffer[polygonIndex].startVertexIndex;

		uint currIndex = startVertexIndex;
		uint prevIndex = batchVertexBuffer[currIndex].prevIndex;

		Vector2 v0 = batchVertexBuffer[prevIndex].position;
		Vector2 v1 = Vector2.zero;

		do
		{
			v1 = batchVertexBuffer[currIndex].position;
			polygonArea += v0.x*v1.y - v1.x*v0.y;
			v0 = v1;

			prevIndex = currIndex;
			currIndex = batchVertexBuffer[currIndex].nextIndex;
		}
		while( currIndex != startVertexIndex );

		return -0.5f * polygonArea;
	}

	static void ProcessPolygons(ref BatchVertex[] batchVertexBuffer, ref BatchPolygon[] batchPolygonBuffer, uint numBatchVertices, uint numBatchPolygons, uint minEdges, uint numProcessingPasses)
	{
		float RedundantEdgeCosineAngle0 = Mathf.Cos( 3.33333f * Mathf.Deg2Rad );

		for( uint polygonIndex=0; polygonIndex<numBatchPolygons; polygonIndex++ )
		{
			InitializeEdges( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex );

			#if SHOW_POLYGON_PROCESSING_PASSES
			uint processingPassesLeft = numProcessingPasses;
			#endif

			#if SHOW_POLYGON_PROCESSING_PASSES
			if( processingPassesLeft == 0 ) continue;
			#endif

			RemoveRedundantEdges( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, RedundantEdgeCosineAngle0 );
			if( batchPolygonBuffer[polygonIndex].numVertices <= minEdges ) continue;

			#if SHOW_POLYGON_PROCESSING_PASSES
			processingPassesLeft--;
			if( processingPassesLeft == 0 ) continue;
			#endif

			const uint CAVITY = 0;
			RemoveLocalConcavity( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, CAVITY );
			if( batchPolygonBuffer[polygonIndex].numVertices <= minEdges ) continue;

			#if SHOW_POLYGON_PROCESSING_PASSES
			processingPassesLeft--;
			if( processingPassesLeft == 0 ) continue;
			#endif

			RemoveRedundantEdges( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, RedundantEdgeCosineAngle0 );
			if( batchPolygonBuffer[polygonIndex].numVertices <= minEdges ) continue;

			#if SHOW_POLYGON_PROCESSING_PASSES
			processingPassesLeft--;
			if( processingPassesLeft == 0 ) continue;
			#endif

			InitializeTriplets( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex );

			uint prevNumVertices = batchPolygonBuffer[polygonIndex].numVertices + 1;
			float polygonReferenceArea = GetPolygonArea( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex );
			float polygonArea = polygonReferenceArea;
			while( prevNumVertices > batchPolygonBuffer[polygonIndex].numVertices && polygonArea > polygonReferenceArea * 0.995f )
			{
				float averageTripletArea;
				float lowerAverageTripletArea;
				float upperAverageTripletArea;

				prevNumVertices = batchPolygonBuffer[polygonIndex].numVertices;
				GetPolygonTripletStatistics( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, out averageTripletArea, out lowerAverageTripletArea, out upperAverageTripletArea );
				RemoveLesserTriplets( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex, lowerAverageTripletArea );
				if( batchPolygonBuffer[polygonIndex].numVertices <= minEdges ) break;

				#if SHOW_POLYGON_PROCESSING_PASSES
				processingPassesLeft--;
				if( processingPassesLeft == 0 ) break;
				#endif

				polygonArea = GetPolygonArea( ref batchVertexBuffer, ref batchPolygonBuffer, polygonIndex );
			}
			if( batchPolygonBuffer[polygonIndex].numVertices <= minEdges ) continue;

			#if SHOW_POLYGON_PROCESSING_PASSES
			if( processingPassesLeft == 0 ) continue;
			#endif		
		}
	}
}