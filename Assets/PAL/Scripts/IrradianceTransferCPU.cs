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
	void RenderBuffers(bool transformChanged, bool intensityChanged)
	{
		_meshAreaLight.PrepareIrradianceTransfer();

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		_offscreenCamera.fieldOfView = OffscreenCameraFOV;

		if( transformChanged )
		{
			Shader.EnableKeyword( "_PAL_ALBEDO" );
			_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
			_offscreenCamera.targetTexture = _albedoBuffer;
			_offscreenCamera.RenderWithShader( _albedoBufferShader, "RenderType" );
			Shader.DisableKeyword( "_PAL_ALBEDO" );

			_offscreenCamera.backgroundColor = new Color( 1, 1, 1, 1 );
			_offscreenCamera.targetTexture = _depthBuffer;
			_offscreenCamera.RenderWithShader( _depthBufferShader, "" );

			_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
			_offscreenCamera.targetTexture = _normalBuffer;
			_offscreenCamera.RenderWithShader( _normalBufferShader, "" );

			_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
			_offscreenCamera.targetTexture = _geometryBuffer;
			_offscreenCamera.RenderWithShader( _geometryBufferShader, "" ); 

			_mergeBufferMaterial.SetTexture( "_NormalBuffer", _normalBuffer );
			_mergeBufferMaterial.SetTexture( "_GeometryBuffer", _geometryBuffer );
			_mergeBufferMaterial.SetVector( "_PixelSize", _irradianceMapPixelSize );
			Graphics.Blit( _normalBuffer, _mergeBuffer, _mergeBufferMaterial, 0 );

			Shader.SetGlobalFloat( "_IlluminationScale", IlluminationBufferIntensityScale );
			_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
			_offscreenCamera.targetTexture = _illuminationBuffer;
			_offscreenCamera.RenderWithShader( _illuminationBufferShader, "" );

			var prevRenderTexture = RenderTexture.active;
			RenderTexture.active = _albedoBuffer;
			_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
			_transferBuffer.Apply();
			RenderTexture.active = prevRenderTexture;
			_albedoBufferPixels = _transferBuffer.GetPixels32();

			prevRenderTexture = RenderTexture.active;
			RenderTexture.active = _depthBuffer;
			_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
			_transferBuffer.Apply();
			RenderTexture.active = prevRenderTexture;
			_depthBufferPixels = _transferBuffer.GetPixels32();

			prevRenderTexture = RenderTexture.active;
			RenderTexture.active = _mergeBuffer;
			_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
			_transferBuffer.Apply();
			RenderTexture.active = prevRenderTexture;
			_mergeBufferPixels = _transferBuffer.GetPixels32();

			prevRenderTexture = RenderTexture.active;
			RenderTexture.active = _illuminationBuffer;
			_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
			_transferBuffer.Apply();
			RenderTexture.active = prevRenderTexture;
			_illuminationBufferPixels = _transferBuffer.GetPixels32();
		}
		else if( intensityChanged )
		{
			Shader.SetGlobalFloat( "_IlluminationScale", IlluminationBufferIntensityScale );
			_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
			_offscreenCamera.targetTexture = _illuminationBuffer;
			_offscreenCamera.RenderWithShader( _illuminationBufferShader, "" );

			var prevRenderTexture = RenderTexture.active;
			RenderTexture.active = _illuminationBuffer;
			_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
			_transferBuffer.Apply();
			RenderTexture.active = prevRenderTexture;
			_illuminationBufferPixels = _transferBuffer.GetPixels32();
		}
	}

	void BuildPolygonMap()
	{
		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		Color32 depthPixel32;
		Color32 mergePixel32;

		_numPolygons = 0;

		// map pixels to polygons

		for( int y=0; y<bufferHeight; y++ )
		{
			int leftMostPixelIndex = y*bufferWidth;

			for( int x=0; x<bufferWidth; x++ )
			{
				int pixelIndex = leftMostPixelIndex + x;
				int leftPixelIndex = ( x > 0 ) ? pixelIndex-1 : -1;
				int lowerPixelIndex = ( y > 0 ) ? pixelIndex-bufferWidth : -1;

				int leftPolygonIndex = ( leftPixelIndex >= 0 ) ? _polygonMap[leftPixelIndex] : -1;
				int lowerPolygonIndex = ( lowerPixelIndex >= 0 ) ? _polygonMap[lowerPixelIndex] : -1;

				depthPixel32 = _depthBufferPixels[pixelIndex];
				mergePixel32 = _mergeBufferPixels[pixelIndex];

				bool leftPixelIsMergeable = ( leftPixelIndex >= 0 ) ? ( mergePixel32.r > 0 ) : ( false );
				bool lowerPixelIsMergeable = ( lowerPixelIndex >= 0 ) ? ( mergePixel32.g > 0 ) : ( false );

				if( depthPixel32.r == 0xFF && depthPixel32.g == 0xFF && depthPixel32.b == 0xFF && depthPixel32.a == 0xFF )
				{
					_polygonMap[pixelIndex] = -1;
				}
				else
				{
					if( leftPolygonIndex >= 0 )
					{
						if( leftPixelIsMergeable )
						{
							_polygonMap[pixelIndex] = leftPolygonIndex;
							if( lowerPixelIsMergeable && leftPolygonIndex > lowerPolygonIndex )
							{
								if( _polygonMergeMap[leftPolygonIndex] == leftPolygonIndex )
								{
									_polygonMergeMap[leftPolygonIndex] = lowerPolygonIndex;
								}
							}
							_polygonSize[_polygonMap[pixelIndex]]++;
						}
						else if( lowerPixelIsMergeable )
						{
							_polygonMap[pixelIndex] = lowerPolygonIndex;
							_polygonSize[_polygonMap[pixelIndex]]++;
						}
						else
						{
							_polygonMap[pixelIndex] = _numPolygons;
							_polygonMergeMap[_numPolygons] = _numPolygons;
							_polygonSize[_numPolygons] = 1;
							_numPolygons++;
						}
					}
					else if( lowerPolygonIndex >= 0 )
					{
						if( lowerPixelIsMergeable )
						{
							_polygonMap[pixelIndex] = lowerPolygonIndex;
							_polygonSize[_polygonMap[pixelIndex]]++;
						}
						else
						{
							_polygonMap[pixelIndex] = _numPolygons;
							_polygonMergeMap[_numPolygons] = _numPolygons;
							_polygonSize[_numPolygons] = 1;
							_numPolygons++;
						}
					}
					else
					{
						_polygonMap[pixelIndex] = _numPolygons;
						_polygonMergeMap[_numPolygons] = _numPolygons;
						_polygonSize[_numPolygons] = 1;
						_numPolygons++;
					}
				}
			}
		}

		// collapse hierarchies of merge map and merge polygons

		for( int i=0; i<_numPolygons; i++ )
		{
			int indexToCollapse = _polygonMergeMap[i];
			while( _polygonMergeMap[indexToCollapse] != indexToCollapse )
			{
				indexToCollapse = _polygonMergeMap[indexToCollapse];
			}

			if( i != indexToCollapse )
			{
				_polygonSize[indexToCollapse] += _polygonSize[i];
				_polygonSize[i] = 0;
				_polygonMergeMap[i] = indexToCollapse;
			}
		}

		for( int i=0; i<_polygonMap.Length; i++ )
		{
			int polygonIndex = _polygonMap[i];
			if( polygonIndex >= 0 )
			{
				_polygonMap[i] = _polygonMergeMap[polygonIndex];
				if( _polygonSize[_polygonMap[i]] < 2 )
				{
					_polygonMap[i] = -1;
				}
			}
		}
	}

	void CreateSecondaryAreaLights(ref PixelCoords marchingSquaresInf, ref PixelCoords marchingSquaresSup)
	{
		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		Color32 irradiancePixel32;
		Color32 albedoPixel32;

		// step 3 : filter out pixels illuminated above the given threshold of bounce intensity

		if( _irradiancePolygons.Length < _numPolygons )
		{
			_irradiancePolygons = new IrradiancePolygon[_numPolygons];
		}

		//for( int i=0; i<_irradiancePolygons.Length; i++ )
		//{
			//_irradiancePolygons[i] = null;
		//}

		for( int y=0; y<bufferHeight; y++ )
		{
			int leftMostPixelIndex = y*bufferWidth;

			for( int x=0; x<bufferWidth; x++ )
			{
				int pixelIndex = leftMostPixelIndex + x;
				int polygonIndex = _polygonMap[pixelIndex];
				if( polygonIndex >= 0 )
				{
					albedoPixel32 = _albedoBufferPixels[pixelIndex];
					irradiancePixel32 = _illuminationBufferPixels[pixelIndex];
					float illumination = irradiancePixel32.r * kDecodeRedFactor + irradiancePixel32.g * kDecodeGreenFactor + irradiancePixel32.b * kDecodeBlueFactor + irradiancePixel32.a * kDecodeAlphaFactor;
					illumination *= 1.0f / IlluminationBufferIntensityScale;
					if( illumination < BounceIntensityTreshold )
					{
						_polygonMap[pixelIndex] = -1;
					}
					else
					{
						IrradiancePolygon irradiancePolygon = _irradiancePolygons[polygonIndex];
						if( irradiancePolygon == null )
						{
							irradiancePolygon = new IrradiancePolygon();
							irradiancePolygon.polygonIndex = polygonIndex;
							_irradiancePolygons[polygonIndex] = irradiancePolygon;
						}
						irradiancePolygon.totalPixels += 1;
						irradiancePolygon.totalIllumination += illumination;
						irradiancePolygon.totalRed += albedoPixel32.r;
						irradiancePolygon.totalGreen += albedoPixel32.g;
						irradiancePolygon.totalBlue += albedoPixel32.b;
						marchingSquaresInf.x = Mathf.Min( marchingSquaresInf.x, x );
						marchingSquaresInf.y = Mathf.Min( marchingSquaresInf.y, y );
						marchingSquaresSup.x = Mathf.Max( marchingSquaresSup.x, x );
						marchingSquaresSup.y = Mathf.Max( marchingSquaresSup.y, y );
					}
				}
			}

			marchingSquaresInf.x = ( marchingSquaresInf.x > 0 ) ? ( marchingSquaresInf.x-1 ) : marchingSquaresInf.x;
			marchingSquaresInf.y = ( marchingSquaresInf.y > 0 ) ? ( marchingSquaresInf.y-1 ) : marchingSquaresInf.y;
			marchingSquaresSup.x = ( marchingSquaresSup.x < bufferWidth-1 ) ? ( marchingSquaresSup.x+1 ) : marchingSquaresSup.x;
			marchingSquaresSup.y = ( marchingSquaresSup.y < bufferHeight-1 ) ? ( marchingSquaresSup.y+1 ) : marchingSquaresSup.y;
		}
	}

	void SmoothPolygon(IrradiancePolygon irradiancePolygon)
	{
		// smooth
		irradiancePolygon.smoothVertices = new Vector3[irradiancePolygon.Vertices.Length];
		float planeFactor = 2 - Mathf.Abs( Vector3.Dot( _offscreenCamera.transform.forward, irradiancePolygon.polygonPlane.normal ) );
		int numSmoothSteps = (int)(IrradiancePolygonSmoothing * planeFactor);
		for( int i=0; i<numSmoothSteps; i++ )
		{
			for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
			{
				int prevIndex = ( index == 0 ) ? irradiancePolygon.Vertices.Length-1 : index-1;
				int nextIndex = ( index == irradiancePolygon.Vertices.Length-1 ) ? 0 : index+1;
				irradiancePolygon.smoothVertices[index] = ( irradiancePolygon.Vertices[prevIndex] + irradiancePolygon.Vertices[index] + irradiancePolygon.Vertices[nextIndex] ) / 3;
			}
			for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
			{
				irradiancePolygon.Vertices[index] = irradiancePolygon.smoothVertices[index];
			}
		}
	}

	int ReduceSemiParallelEdges(IrradiancePolygon irradiancePolygon)
	{
		// reduce semi-parallel edges

		const float MinEdgeAngle = 15.0f;
		const float ReductionAngle = 1.0f;

		irradiancePolygon.vertexFlags = new bool[irradiancePolygon.Vertices.Length];
		for( int index=0; index<irradiancePolygon.vertexFlags.Length; index++ ) 
		{
			irradiancePolygon.vertexFlags[index] = true;
		}

		float reductionAngle = 0.0f;
		float reductionCosAngle = Mathf.Cos( reductionAngle * Mathf.Deg2Rad );
		int numVertices = irradiancePolygon.Vertices.Length;
		while( reductionAngle < MinEdgeAngle && numVertices > 4 )
		{
			int prevIndex = -1;
			for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
			{
				if( irradiancePolygon.vertexFlags[index] )
				{
					prevIndex = index;
					break;
				}
			}
			if( prevIndex < 0 )
			{
				Debug.LogError( "prevIndex < 0" );
				break;
			}

			int currIndex = -1;
			for( int index=prevIndex+1; index<irradiancePolygon.Vertices.Length; index++ )
			{
				if( irradiancePolygon.vertexFlags[index] )
				{
					currIndex = index;
					break;
				}
			}
			if( currIndex < 0 )
			{
				Debug.LogError( "currIndex < 0" );
				break;
			}

			int nextIndex = -1;
			for( int index=currIndex+1; index<irradiancePolygon.Vertices.Length; index++ )
			{
				if( irradiancePolygon.vertexFlags[index] )
				{
					nextIndex = index;
					break;
				}
			}
			if( nextIndex < 0 )
			{
				Debug.LogError( "nextIndex < 0" );
				break;
			}

			do
			{
				float cosAngle = Vector3.Dot( ( irradiancePolygon.Vertices[currIndex] - irradiancePolygon.Vertices[prevIndex] ).normalized, ( irradiancePolygon.Vertices[nextIndex] - irradiancePolygon.Vertices[currIndex] ).normalized );
				if( cosAngle >= reductionCosAngle )
				{
					irradiancePolygon.vertexFlags[currIndex] = false;
					numVertices--;
				}

				if( nextIndex < prevIndex )
				{
					break;
				}

				prevIndex = currIndex;
				currIndex = nextIndex;
				nextIndex = -1;

				for( int index=currIndex+1; index<irradiancePolygon.Vertices.Length; index++ )
				{
					if( irradiancePolygon.vertexFlags[index] )
					{
						nextIndex = index;
						break;
					}
				}

				if( nextIndex < 0 )
				{
					for( int index=0; index<prevIndex; index++ )
					{
						if( irradiancePolygon.vertexFlags[index] )
						{
							nextIndex = index;
							break;
						}
					}

					if( nextIndex < 0 )
					{
						Debug.LogError( "nextIndex < 0 (in loop)" );
						break;
					}
				}
			}
			while( numVertices > 4 );

			reductionAngle += ReductionAngle;
			reductionCosAngle = Mathf.Cos( reductionAngle * Mathf.Deg2Rad );
		}

		return numVertices;
	}

	void CompressArrays(IrradiancePolygon irradiancePolygon, int numVertices)
	{
		irradiancePolygon.Vertices = new Vector3[numVertices];
		int reducedIndex = 0;
		for( int index=0; index<irradiancePolygon.smoothVertices.Length; index++ )
		{
			if( irradiancePolygon.vertexFlags[index] )
			{
				irradiancePolygon.Vertices[reducedIndex] = irradiancePolygon.smoothVertices[index];
				reducedIndex++;
			}
		}
	}

	void CombineVertices(IrradiancePolygon irradiancePolygon, int numVertices)
	{
		irradiancePolygon.Vertices = new Vector3[numVertices];

		int originalIndex = 0;
		int reducedIndex = 0;
		while( originalIndex < irradiancePolygon.vertexFlags.Length )
		{
			if( irradiancePolygon.vertexFlags[originalIndex] ) 
			{
				int averagingStep = 0;
				Vector3 averageVertex = Vector3.zero;
				while( originalIndex < irradiancePolygon.vertexFlags.Length && irradiancePolygon.vertexFlags[originalIndex] )
				{
					Vector3 nextAverageVertex = ( averageVertex * averagingStep + irradiancePolygon.smoothVertices[originalIndex] ) / ( averagingStep + 1 );

					if( averagingStep > 2 )
					{
						bool combinationIsValid = true;
						for( int index=originalIndex-averagingStep; index<=originalIndex; index++ )
						{
							float distanceToAverageVertex = Vector3.Distance( irradiancePolygon.smoothVertices[index], nextAverageVertex );
							float distanceBetweenVertices = ( index < originalIndex ) ? Vector3.Distance( irradiancePolygon.smoothVertices[index], irradiancePolygon.smoothVertices[index+1] ) : Vector3.Distance( irradiancePolygon.smoothVertices[index], irradiancePolygon.smoothVertices[index-1] );
							if( distanceToAverageVertex > distanceBetweenVertices )
							{
								combinationIsValid = false;
								break;
							}
						}

						if( !combinationIsValid ) break;
					}

					averagingStep++;
					averageVertex = nextAverageVertex;
					originalIndex++;
					numVertices--;
					if( ( numVertices < 3 && reducedIndex < 1 ) || ( numVertices < 2 && reducedIndex < 2 ) ) break;
				}

				irradiancePolygon.Vertices[reducedIndex] = averageVertex;
				reducedIndex++;
			}
			else
			{
				originalIndex++;
			}
		}

		if( reducedIndex < irradiancePolygon.Vertices.Length )
		{
			System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, reducedIndex );
		}
	}

	void EmbarassedMarchingSquares(PixelCoords marchingSquaresInf, PixelCoords marchingSquaresSup)
	{
		// build isolines using marching squares

		const ushort Zero = 0;
		const ushort BitMask0 = 8;
		const ushort BitMask1 = 4;
		const ushort BitMask2 = 2;
		const ushort BitMask3 = 1;

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		float farClipPlane = _offscreenCamera.farClipPlane;

		int numPlanePixelCoords = 0;
		PixelCoords[] planePixels = new PixelCoords[] { PixelCoords.zero, PixelCoords.zero, PixelCoords.zero };
		Vector3[] worldSpacePixelPos = new Vector3[] { Vector3.zero, Vector3.zero, Vector3.zero };
		Vector3 viewPortSpacePixelPos = Vector3.zero;
		Vector3 worldSpacePlaneNormal = Vector3.zero;
		Color32 depthPixel32;

		for( int polygonIndex=0; polygonIndex<_irradiancePolygons.Length; polygonIndex++ )
		{
			var irradiancePolygon = _irradiancePolygons[polygonIndex];
			if( irradiancePolygon == null ) continue;

			// fill threshold map & calculate world space normal of polygon

			numPlanePixelCoords = 0;

			for( int y=marchingSquaresInf.y; y<=marchingSquaresSup.y; y++ )
			{
				int leftMostPixelIndex = y*bufferWidth;
				for( int x=marchingSquaresInf.x; x<=marchingSquaresSup.x; x++ )
				{
					int pixelIndex = leftMostPixelIndex + x;

					if( _polygonMap[pixelIndex] == polygonIndex && x > 0 && y > 0 && x < bufferWidth-1 && y < bufferHeight-1 )
					{
						_thresholdMap[pixelIndex] = true;

						if( numPlanePixelCoords < 3 )
						{
							planePixels[numPlanePixelCoords].x = x;
							planePixels[numPlanePixelCoords].y = y;
							depthPixel32 = _depthBufferPixels[pixelIndex];
							viewPortSpacePixelPos.x = ( planePixels[numPlanePixelCoords].x + 0.5f ) * _irradianceMapInvBufferResolution.x;
							viewPortSpacePixelPos.y = ( planePixels[numPlanePixelCoords].y + 0.5f ) * _irradianceMapInvBufferResolution.y;
							viewPortSpacePixelPos.z = depthPixel32.r * kDecodeRedFactor + depthPixel32.g * kDecodeGreenFactor + depthPixel32.b * kDecodeBlueFactor + depthPixel32.a * kDecodeAlphaFactor;
							viewPortSpacePixelPos.z = viewPortSpacePixelPos.z * farClipPlane;
							worldSpacePixelPos[numPlanePixelCoords] = _offscreenCamera.ViewportToWorldPoint( viewPortSpacePixelPos );

							numPlanePixelCoords++;
							if( numPlanePixelCoords == 3 )
							{
								if( ( planePixels[0].x == planePixels[1].x && planePixels[1].x == planePixels[2].x ) ||
									( planePixels[0].y == planePixels[1].y && planePixels[1].y == planePixels[2].y ) )
								{
									numPlanePixelCoords--;
								}
								else
								{
									Vector3 worldSpacePlaneTangent0 = ( worldSpacePixelPos[1] - worldSpacePixelPos[0] ).normalized;
									Vector3 worldSpacePlaneTangent1 = ( worldSpacePixelPos[2] - worldSpacePixelPos[0] ).normalized;
									if( Mathf.Abs( Vector3.Dot( worldSpacePlaneTangent0, worldSpacePlaneTangent1 ) ) > 0.9f )
									{
										numPlanePixelCoords--;
									}
								}
							}

						}
					}
					else
					{
						_thresholdMap[pixelIndex] = false;
					}
				}
			}

			if( numPlanePixelCoords == 3 )
			{
				worldSpacePlaneNormal = Vector3.Cross( (worldSpacePixelPos[1]-worldSpacePixelPos[0]).normalized, (worldSpacePixelPos[2]-worldSpacePixelPos[0]).normalized ).normalized;
				worldSpacePlaneNormal *= -Mathf.Sign( Vector3.Dot( _offscreenCamera.transform.forward, worldSpacePlaneNormal ) );
				irradiancePolygon.polygonPlane.SetNormalAndPosition( worldSpacePlaneNormal, worldSpacePixelPos[0] );
			}
			else
			{
				// edge case : all the pixels lay on the same line
				continue;
			}

			// fill contour map

			int leftMostXCoord = -1;
			int leftMostContourIndex = -1;
			int numContourCells = 0;
			for( int y=marchingSquaresInf.y; y<marchingSquaresSup.y; y++ )
			{
				int leftMostCellIndex = y*(bufferWidth-1);
				for( int x=marchingSquaresInf.x; x<marchingSquaresSup.x; x++ )
				{
					int contourIndex = leftMostCellIndex+x;

					int pixelIndex0 = (y+1)*bufferWidth + x;
					int pixelIndex1 = (y+1)*bufferWidth + (x+1);
					int pixelIndex2 = y*bufferWidth + (x+1);
					int pixelIndex3 = y*bufferWidth + x;

					bool thresholdValue0 = _thresholdMap[pixelIndex0];
					bool thresholdValue1 = _thresholdMap[pixelIndex1];
					bool thresholdValue2 = _thresholdMap[pixelIndex2];
					bool thresholdValue3 = _thresholdMap[pixelIndex3];

					ushort lookupIndex = 0;
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
				}
			}

			if( leftMostContourIndex < 0 )
			{
				Debug.LogError( "[IrradianceTransfer] leftMostContourIndex == " + leftMostContourIndex + "!" );
				continue;
			}

			// allocate array for vertices
			irradiancePolygon.Vertices = new Vector3[numContourCells];

			// transform contour to world space

			const int Start = 0;
			const int Left = 1;
			const int Up = 2;
			const int Right = 3;
			const int Down = 4;
			const int Stop = -1;

			int numVertices = 0;
			int currentContourIndex = leftMostContourIndex;
			int move = Start;

			Vector3 viewPortSpaceVertexPos = Vector3.zero;

			while( move != Stop )
			{
				viewPortSpaceVertexPos.x = currentContourIndex % (bufferWidth-1) + 0.5f;
				viewPortSpaceVertexPos.y = currentContourIndex / (bufferWidth-1) + 0.5f;

				ushort currentContourValue = _contourMap[currentContourIndex];

				switch( currentContourValue )
				{
				case 1:
					viewPortSpaceVertexPos.x += OutlineOffset;
					move = ( move == Right ) ? Down : Stop;
					break;
				case 2:
					viewPortSpaceVertexPos.x += 1;
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Up || move == Start ) ? Right : Stop;
					break;
				case 3:
					viewPortSpaceVertexPos.x += 1;
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Right ) ? Right : Stop;
					break;
				case 4:
					viewPortSpaceVertexPos.x += 1 - OutlineOffset;
					viewPortSpaceVertexPos.y += 1;
					move = ( move == Left || move == Start ) ? Up : Stop;
					break;
				case 5:
					viewPortSpaceVertexPos.x += ( move == Right ) ? OutlineOffset : ( 1 - OutlineOffset );
					viewPortSpaceVertexPos.y += ( move == Right ) ? 1 : 0;
					move = ( move == Right ) ? Up : ( ( move == Left ) ? Down : Stop );
					break;
				case 6:
					viewPortSpaceVertexPos.x += 1 - OutlineOffset;
					viewPortSpaceVertexPos.y += 1;
					move = ( move == Up || move == Start ) ? Up : Stop;
					break;
				case 7:
					viewPortSpaceVertexPos.x += OutlineOffset;
					viewPortSpaceVertexPos.y += 1;
					move = ( move == Right ) ? Up : Stop;
					break;
				case 8:
					viewPortSpaceVertexPos.y += 1 - OutlineOffset;
					move = ( move == Down ) ? Left : Stop;
					break;
				case 9:
					viewPortSpaceVertexPos.x += OutlineOffset;
					move = ( move == Down ) ? Down : Stop;
					break;
				case 10:
					viewPortSpaceVertexPos.x += ( move == Down ) ? 1 : 0;
					viewPortSpaceVertexPos.y += ( move == Down ) ? ( 1 - OutlineOffset ) : OutlineOffset;
					move = ( move == Down ) ? Right : ( ( move == Up ) ? Left : Stop );
					break;
				case 11:
					viewPortSpaceVertexPos.x += 1;
					viewPortSpaceVertexPos.y += 1 - OutlineOffset;
					move = ( move == Down ) ? Right : Stop;
					break;
				case 12:
					viewPortSpaceVertexPos.y += 1 - OutlineOffset;
					move = ( move == Left ) ? Left : Stop;
					break;
				case 13:
					viewPortSpaceVertexPos.x += 1 - OutlineOffset;
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
				float distance = float.MaxValue;
				if( irradiancePolygon.polygonPlane.Raycast( ray, out distance ) )
				{
					irradiancePolygon.Vertices[numVertices] = ray.origin + ray.direction * distance;
					numVertices++;
				}
			}

			if( irradiancePolygon.Vertices.Length > numVertices )
			{
				System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, numVertices );
			}
		}
	}
}