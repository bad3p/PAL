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

#define GAUSSIAN_SMOOTH
#undef GAUSSIAN_SMOOTH

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
	#region Shared
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

		float farClipPlane = _offscreenCamera.farClipPlane;

		Color32 depthPixel32;
		Color32 irradiancePixel32;
		Color32 albedoPixel32;
		Vector3 viewPortSpacePixelPos = Vector3.zero;
		Vector3 worldSpacePlaneTangent0 = Vector3.zero;
		Vector3 worldSpacePlaneTangent1 = Vector3.zero;

		// step 3 : filter out pixels illuminated above the given threshold of bounce intensity

		if( _irradiancePolygons.Length < _numPolygons )
		{
			_irradiancePolygons = new IrradiancePolygon[_numPolygons];
		}

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

						irradiancePolygon.marchingSquaresInf.x = irradiancePolygon.marchingSquaresInf.x > x ? x : irradiancePolygon.marchingSquaresInf.x;
						irradiancePolygon.marchingSquaresInf.y = irradiancePolygon.marchingSquaresInf.y > y ? y : irradiancePolygon.marchingSquaresInf.y;
						irradiancePolygon.marchingSquaresSup.x = irradiancePolygon.marchingSquaresSup.x < x ? x : irradiancePolygon.marchingSquaresSup.x;
						irradiancePolygon.marchingSquaresSup.y = irradiancePolygon.marchingSquaresSup.y < y ? y : irradiancePolygon.marchingSquaresSup.y;

						if( irradiancePolygon.leftmostPixelCoords.x > x )
						{
							irradiancePolygon.leftmostPixelCoords.x = x;
							irradiancePolygon.leftmostPixelCoords.y = y;
						}

						if( irradiancePolygon.numPlanePixels < 3 )
						{
							irradiancePolygon.planePixels[irradiancePolygon.numPlanePixels].x = x;
							irradiancePolygon.planePixels[irradiancePolygon.numPlanePixels].y = y;
							depthPixel32 = _depthBufferPixels[pixelIndex];
							viewPortSpacePixelPos.x = ( x + 0.5f ) * _irradianceMapInvBufferResolution.x;
							viewPortSpacePixelPos.y = ( y + 0.5f ) * _irradianceMapInvBufferResolution.y;
							viewPortSpacePixelPos.z = depthPixel32.r * kDecodeRedFactor + depthPixel32.g * kDecodeGreenFactor + depthPixel32.b * kDecodeBlueFactor + depthPixel32.a * kDecodeAlphaFactor;
							viewPortSpacePixelPos.z = viewPortSpacePixelPos.z * farClipPlane;
							irradiancePolygon.worldSpacePixelPos[irradiancePolygon.numPlanePixels] = _offscreenCamera.ViewportToWorldPoint( viewPortSpacePixelPos );

							irradiancePolygon.numPlanePixels++;
							if( irradiancePolygon.numPlanePixels == 3 )
							{
								if( ( irradiancePolygon.planePixels[0].x == irradiancePolygon.planePixels[1].x && irradiancePolygon.planePixels[1].x == irradiancePolygon.planePixels[2].x ) ||
									( irradiancePolygon.planePixels[0].y == irradiancePolygon.planePixels[1].y && irradiancePolygon.planePixels[1].y == irradiancePolygon.planePixels[2].y ) )
								{
									irradiancePolygon.numPlanePixels--;
								}
								else
								{
									worldSpacePlaneTangent0 = ( irradiancePolygon.worldSpacePixelPos[1] - irradiancePolygon.worldSpacePixelPos[0] ).normalized;
									worldSpacePlaneTangent1 = ( irradiancePolygon.worldSpacePixelPos[2] - irradiancePolygon.worldSpacePixelPos[0] ).normalized;
										
									const float AngleCosineThreshold = 0.9659258262890682867497431997289f;
									if( Mathf.Abs( Vector3.Dot( worldSpacePlaneTangent0, worldSpacePlaneTangent1 ) ) > AngleCosineThreshold )
									{
										irradiancePolygon.numPlanePixels--;
									}
								}
							}
						}

						marchingSquaresInf.x = marchingSquaresInf.x > x ? x : marchingSquaresInf.x;
						marchingSquaresInf.y = marchingSquaresInf.y > y ? y : marchingSquaresInf.y;
						marchingSquaresSup.x = marchingSquaresSup.x < x ? x : marchingSquaresSup.x;
						marchingSquaresSup.y = marchingSquaresSup.y < y ? y : marchingSquaresSup.y;
					}
				}
			}

			marchingSquaresInf.x = ( marchingSquaresInf.x > 0 ) ? ( marchingSquaresInf.x-1 ) : marchingSquaresInf.x;
			marchingSquaresInf.y = ( marchingSquaresInf.y > 0 ) ? ( marchingSquaresInf.y-1 ) : marchingSquaresInf.y;
			marchingSquaresSup.x = ( marchingSquaresSup.x < bufferWidth-1 ) ? ( marchingSquaresSup.x+1 ) : marchingSquaresSup.x;
			marchingSquaresSup.y = ( marchingSquaresSup.y < bufferHeight-1 ) ? ( marchingSquaresSup.y+1 ) : marchingSquaresSup.y;
		}
	}
	#endregion

	#region MarchingSquaresCPU
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
		Vector3 worldSpacePlaneNormal = Vector3.zero;

		for( int polygonIndex=0; polygonIndex<_irradiancePolygons.Length; polygonIndex++ )
		{
			var irradiancePolygon = _irradiancePolygons[polygonIndex];
			if( irradiancePolygon == null ) continue;

			// location of this polygon's vertices in batch buffer
			irradiancePolygon.BatchIndex = _numBatchVertices;

			// calculate plane of polygon in world space

			if( irradiancePolygon.numPlanePixels == 3 )
			{
				worldSpacePlaneNormal = Vector3.Cross( (irradiancePolygon.worldSpacePixelPos[1]-irradiancePolygon.worldSpacePixelPos[0]).normalized, (irradiancePolygon.worldSpacePixelPos[2]-irradiancePolygon.worldSpacePixelPos[0]).normalized ).normalized;
				worldSpacePlaneNormal *= -Mathf.Sign( Vector3.Dot( ( irradiancePolygon.worldSpacePixelPos[0] - _offscreenCamera.transform.position ).normalized, worldSpacePlaneNormal ) );
				irradiancePolygon.polygonPlaneNormal = worldSpacePlaneNormal;
				irradiancePolygon.pointOnPolygonPlane = irradiancePolygon.worldSpacePixelPos[0];
			}
			else
			{
				// edge case : all the pixels lay on the same line
				_irradiancePolygons[polygonIndex] = null;
				continue;
			}

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

				_writeOnlyVertexBuffer.vertices[_numBatchVertices].flag = 1;
				_writeOnlyVertexBuffer.vertices[_numBatchVertices].position = ( rayOrigin + rayDirection * distance );
				_writeOnlyVertexBuffer.vertices[_numBatchVertices].localIndex = (uint)numPolygonVertices;
				_writeOnlyVertexBuffer.vertices[_numBatchVertices].lastLocalIndex = 0;

				_numBatchVertices++;
				numPolygonVertices++;

				if( _numBatchVertices >= _writeOnlyVertexBuffer.Length )
				{
					_writeOnlyVertexBuffer.Resize( _writeOnlyVertexBuffer.Length + GPUGroupSize );
				}
			}

			irradiancePolygon.Vertices = new Vector3[numPolygonVertices];

			// complete vertex data
			for( int j=irradiancePolygon.BatchIndex; j<irradiancePolygon.BatchIndex+numPolygonVertices; j++ )
			{
				_writeOnlyVertexBuffer.vertices[j].lastLocalIndex = (uint)( numPolygonVertices-1 );
			}

			numPolygonVertices = 0;
		}

		if( _readOnlyVertexBuffer.Length != _writeOnlyVertexBuffer.Length )
		{
			_readOnlyVertexBuffer.Resize( _writeOnlyVertexBuffer.Length );
		}

		PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );

		ProcessPolygonsCPU();
	}

	#if SHOW_POLYGON_PROCESSING_PASSES
	int _polygonProcessingPass = 0;
	#endif

	#if SHOW_VERTEX_LABELS
	List<KeyValuePair<Vector3,string>> _vertexLabels = new List<KeyValuePair<Vector3,string>>();
	#endif

	void DrawVertexLabels()
	{
		#if SHOW_VERTEX_LABELS		
			foreach( var vertexLabel in _vertexLabels )
			{
				UnityEditor.Handles.Label( vertexLabel.Key, vertexLabel.Value );
			}
		#endif
	}

	void ProcessPolygonsCPU()
	{
		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass == 0 )
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
		}
		#endif

		// smooth projected vertices

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 1 )
		#endif
		{
			int totalSmoothSteps = GPUSmoothSteps + (int)(Resolution);
			for( int smoothStep=0; smoothStep<totalSmoothSteps; smoothStep++ )
			{
				if( smoothStep > 0 )
				{
					PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
				}
				SmoothVerticesCPU( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices );
			}
		}

		// reduce semi-parallel edges, pass 0

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 2  )
		#endif
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			ReduceSemiParallelEdges( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, Mathf.Cos( Mathf.Deg2Rad * GPUSemiParallelEdgeAngle0 ) );
		}

		// merge even vertices

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 3  )
		#endif
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			MergeEvenVertices( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, GPUMergeEvenVerticesThreshold );
		}

		// reduce semi-parallel edges, pass 1

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 4  )
		#endif
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			ReduceSemiParallelEdges( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, Mathf.Cos( Mathf.Deg2Rad * GPUSemiParallelEdgeAngle1 ) );
		}

		// merge sparse vertices

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 5 )
		#endif
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			MergeSparseVertices( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, GPUMergeSparseVerticesThreshold, 2 );
		}

		// reduce semi-parallel edges, pass 2

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 6  )
		#endif
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			ReduceSemiParallelEdges( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, Mathf.Cos( Mathf.Deg2Rad * GPUSemiParallelEdgeAngle2 ) );
		}

		// remove lesser edges, pass 0

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 7  )
		#endif
		{			
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			RemoveLesserEdges( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, Mathf.Cos( Mathf.Deg2Rad * GPUEdgeAngle0 ), GPUEdgeRatio0 );
		}

		// remove lesser edges, pass 1

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 8  )
		#endif
		{
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			RemoveLesserEdges( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, Mathf.Cos( Mathf.Deg2Rad * GPUEdgeAngle1 ), GPUEdgeRatio1 );
		}

		// remove lesser edges, pass 2

		#if SHOW_POLYGON_PROCESSING_PASSES
		if( _polygonProcessingPass >= 9  )
		#endif
		{			
			PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );
			RemoveLesserEdges( ref _readOnlyVertexBuffer.vertices, ref _writeOnlyVertexBuffer.vertices, _numBatchVertices, Mathf.Cos( Mathf.Deg2Rad * GPUEdgeAngle2 ), GPUEdgeRatio2 );
		}

		#if SHOW_POLYGON_PROCESSING_PASSES
			_polygonProcessingPass++;
			if( _polygonProcessingPass >= 10 ) _polygonProcessingPass = 0;
		#endif

		// get data 

		PALUtils.Swap<VertexBuffer>( ref _readOnlyVertexBuffer, ref _writeOnlyVertexBuffer );

		#if SHOW_VERTEX_LABELS
			_vertexLabels.Clear();
		#endif

		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			IrradiancePolygon irradiancePolygon = _irradiancePolygons[i];
			if( irradiancePolygon == null ) continue;

			int numPolygonVertices = irradiancePolygon.Vertices.Length;
			int numReducedVertices = 0;
			int startVertex = irradiancePolygon.BatchIndex;

			for( int j=0; j<numPolygonVertices; j++ )
			{
				if( _readOnlyVertexBuffer.vertices[startVertex+j].flag == 1 )
				{
					irradiancePolygon.Vertices[numReducedVertices] = _readOnlyVertexBuffer.vertices[startVertex+j].position;
					numReducedVertices++;

					#if SHOW_VERTEX_LABELS
					_vertexLabels.Add( new KeyValuePair<Vector3,string>( _readOnlyVertexBuffer.vertices[startVertex+j].position, _readOnlyVertexBuffer.vertices[startVertex+j].localIndex.ToString() ) );
					#endif
				}
			}

			if( numReducedVertices < numPolygonVertices )
			{
				System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, numReducedVertices );
			}
		}
	}

	static uint GetPrevIndex(uint index, ref Vertex[] inBuffer)
	{
		if( inBuffer[index].localIndex == 0 )
		{
			return index + inBuffer[index].lastLocalIndex;
		}
		else
		{
			return index-1;
		}
	}

	static uint GetNextIndex(uint index, ref Vertex[] inBuffer)
	{
		if( inBuffer[index].localIndex == inBuffer[index].lastLocalIndex )
		{
			return index - inBuffer[index].lastLocalIndex;
		}
		else
		{
			return index+1;
		}
	}

	static uint GetPrevSparseIndex(uint index, uint sparseness, ref Vertex[] inBuffer)
	{
		for( uint i=0; i<sparseness; i++ )
		{
			if( inBuffer[index].localIndex == 0 )
			{
				index = index + inBuffer[index].lastLocalIndex;
			}
			else
			{
				index = index-1;
			}
		}
		return index;
	}

	static uint GetNextSparseIndex(uint index, uint sparseness, ref Vertex[] inBuffer)
	{
		for( uint i=0; i<sparseness; i++ )
		{
			if( inBuffer[index].localIndex == inBuffer[index].lastLocalIndex )
			{
				index = index - inBuffer[index].lastLocalIndex;
			}
			else
			{
				index = index+1;
			}
		}
		return index;
	}

	static uint GetPrevIndexWithFlag(uint index, uint flag, ref Vertex[] inBuffer)
	{
		uint prevIndex = GetPrevIndex( index, ref inBuffer );
		while( prevIndex != index && inBuffer[prevIndex].flag != flag )
		{
			prevIndex = GetPrevIndex( prevIndex, ref inBuffer );
		}
		return prevIndex;		
	}

	static uint GetNextIndexWithFlag(uint index, uint flag, ref Vertex[] inBuffer)
	{
		uint nextIndex = GetNextIndex( index, ref inBuffer );
		while( nextIndex != index && inBuffer[nextIndex].flag != flag )
		{
			nextIndex = GetNextIndex( nextIndex, ref inBuffer );
		}
		return nextIndex;	
	}

	static void SmoothVerticesCPU(ref Vertex[] inBuffer, ref Vertex[] outBuffer, int numBatchVertices)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			uint prevIndex = ( inBuffer[index].localIndex == 0 ) ? ( index + inBuffer[index].lastLocalIndex ) : ( index-1 );
			uint nextIndex = ( inBuffer[index].localIndex == inBuffer[index].lastLocalIndex ) ? ( index - inBuffer[index].lastLocalIndex ) : ( index+1 );

			#if GAUSSIAN_SMOOTH
				uint precedingIndex = ( inBuffer[prevIndex].localIndex == 0 ) ? ( prevIndex + inBuffer[prevIndex].lastLocalIndex ) : ( prevIndex-1 );
				uint succedingIndex = ( inBuffer[nextIndex].localIndex == inBuffer[nextIndex].lastLocalIndex ) ? ( nextIndex - inBuffer[nextIndex].lastLocalIndex ) : ( nextIndex+1 );

				outBuffer[index].position = 
				(
					inBuffer[precedingIndex].position * 0.0702702703f +
					inBuffer[prevIndex].position * 0.3162162162f +
					inBuffer[index].position * 0.2270270270f +
					inBuffer[nextIndex].position * 0.3162162162f +
					inBuffer[succedingIndex].position * 0.0702702703f
				);
			#else				
				outBuffer[index].position = ( inBuffer[prevIndex].position + inBuffer[index].position + inBuffer[nextIndex].position ) / 3.0f;
			#endif

			outBuffer[index].flag = inBuffer[index].flag;
			outBuffer[index].localIndex = inBuffer[index].localIndex;
			outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
		}
	}

	static void ReduceSemiParallelEdges(ref Vertex[] inBuffer, ref Vertex[] outBuffer, int numBatchVertices, float thresholdAngleCosine)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			if( inBuffer[index].flag == 1 )
			{
				uint prevIndex = GetPrevIndexWithFlag( index, 1, ref inBuffer );
				uint nextIndex = GetNextIndexWithFlag( index, 1, ref inBuffer );

				Vector3 prevPosition = inBuffer[prevIndex].position;
				Vector3 currPosition = inBuffer[index].position;
				Vector3 nextPosition = inBuffer[nextIndex].position;

				Vector3 prevEdge = ( currPosition - prevPosition ).normalized;
				Vector3 currEdge = ( nextPosition - currPosition ).normalized;

				float edgeAngleCosine = Vector3.Dot( prevEdge, currEdge );

				if( edgeAngleCosine > thresholdAngleCosine )
				{
					outBuffer[index].flag = 0;
				}
				else
				{
					outBuffer[index].flag = inBuffer[index].flag;
				}
				outBuffer[index].localIndex = inBuffer[index].localIndex;
				outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				outBuffer[index].position = inBuffer[index].position;
			}
			else
			{
				outBuffer[index].flag = inBuffer[index].flag;
				outBuffer[index].localIndex = inBuffer[index].localIndex;
				outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				outBuffer[index].position = inBuffer[index].position;
			}
		}
	}

	static void MergeEvenVertices(ref Vertex[] inBuffer, ref Vertex[] outBuffer, int numBatchVertices, uint thresholdLastLocalIndex)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			if( inBuffer[index].flag == 1 && inBuffer[index].lastLocalIndex + 1 > thresholdLastLocalIndex )
			{
				if( inBuffer[index].localIndex % 2 == 0 )
				{
					uint prevIndex = GetPrevIndex( index, ref inBuffer );
					uint nextIndex = GetNextIndex( index, ref inBuffer );

					if( inBuffer[prevIndex].flag == 1 && inBuffer[nextIndex].flag == 1 )
					{
						outBuffer[index].position = ( inBuffer[prevIndex].position + inBuffer[index].position + inBuffer[nextIndex].position ) / 3;
					}
					else if( inBuffer[prevIndex].flag == 1 )
					{
						outBuffer[index].position = ( inBuffer[prevIndex].position + inBuffer[index].position ) / 2;
					}
					else if( inBuffer[nextIndex].flag == 1 )
					{
						outBuffer[index].position = ( inBuffer[nextIndex].position + inBuffer[index].position ) / 2;
					}

					outBuffer[index].flag = inBuffer[index].flag;
					outBuffer[index].localIndex = inBuffer[index].localIndex;
					outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				}
				else
				{
					outBuffer[index].flag = 0;
					outBuffer[index].localIndex = inBuffer[index].localIndex;
					outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
					outBuffer[index].position = inBuffer[index].position;
				}
			}
			else
			{
				outBuffer[index].flag = inBuffer[index].flag;
				outBuffer[index].localIndex = inBuffer[index].localIndex;
				outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				outBuffer[index].position = inBuffer[index].position;
			}
		}
	}

	static void MergeSparseVertices(ref Vertex[] inBuffer, ref Vertex[] outBuffer, int numBatchVertices, uint thresholdLastLocalIndex, uint sparseness)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			if( inBuffer[index].flag == 1 && inBuffer[index].lastLocalIndex + 1 > thresholdLastLocalIndex )
			{
				uint sparseIndex = inBuffer[index].localIndex / sparseness;
				if( sparseIndex % 2 == 0 )
				{
					uint prevIndex = GetPrevSparseIndex( index, sparseness, ref inBuffer );
					uint nextIndex = GetNextSparseIndex( index, sparseness, ref inBuffer );

					if( inBuffer[prevIndex].flag == 1 && inBuffer[nextIndex].flag == 1 )
					{
						outBuffer[index].position = ( inBuffer[prevIndex].position + inBuffer[index].position + inBuffer[nextIndex].position ) / 3;
					}
					else if( inBuffer[prevIndex].flag == 1 )
					{
						outBuffer[index].position = ( inBuffer[prevIndex].position + inBuffer[index].position ) / 2;
					}
					else if( inBuffer[nextIndex].flag == 1 )
					{
						outBuffer[index].position = ( inBuffer[nextIndex].position + inBuffer[index].position ) / 2;
					}

					outBuffer[index].flag = inBuffer[index].flag;
					outBuffer[index].localIndex = inBuffer[index].localIndex;
					outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				}
				else
				{
					outBuffer[index].flag = 0;
					outBuffer[index].localIndex = inBuffer[index].localIndex;
					outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
					outBuffer[index].position = inBuffer[index].position;
				}
			}
			else
			{
				outBuffer[index].flag = inBuffer[index].flag;
				outBuffer[index].localIndex = inBuffer[index].localIndex;
				outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				outBuffer[index].position = inBuffer[index].position;
			}
		}
	}

	static void RemoveLesserEdges(ref Vertex[] inBuffer, ref Vertex[] outBuffer, int numBatchVertices, float thresholdAngleCosine, float thresholdEdgeRatio)
	{
		for( uint index=0; index<numBatchVertices; index++ )
		{
			if( inBuffer[index].flag == 1 )
			{
				uint prevIndex = GetPrevIndexWithFlag( index, 1, ref inBuffer );
				uint nextIndex = GetNextIndexWithFlag( index, 1, ref inBuffer );
				uint precedingIndex = GetPrevIndexWithFlag( prevIndex, 1, ref inBuffer );
				uint succedingIndex = GetNextIndexWithFlag( nextIndex, 1, ref inBuffer );

				Vector3 prevPosition = inBuffer[prevIndex].position;
				Vector3 currPosition = inBuffer[index].position;
				Vector3 nextPosition = inBuffer[nextIndex].position;
				Vector3 precedingPosition = inBuffer[precedingIndex].position;
				Vector3 succedingPosition = inBuffer[succedingIndex].position;

				Vector3 prevEdge = ( currPosition - prevPosition );
				Vector3 currEdge = ( nextPosition - currPosition );
				Vector3 nextEdge = ( succedingPosition - nextPosition );

				float prevEdgeLength = ( prevEdge ).magnitude;
				float currEdgeLength = ( currEdge ).magnitude;
				float nextEdgeLength = ( nextEdge ).magnitude;

				if( prevEdgeLength * thresholdEdgeRatio >= currEdgeLength || nextEdgeLength * thresholdEdgeRatio >= currEdgeLength )
				{
					prevEdge *= 1.0f / prevEdgeLength;
					nextEdge *= 1.0f / nextEdgeLength;

					float edgeAngleCosine = Vector3.Dot( prevEdge, nextEdge );

					if( edgeAngleCosine > thresholdAngleCosine )
					{
						outBuffer[index].position = ( currPosition + nextPosition ) / 2;
					}
					else
					{				
						outBuffer[index].position = inBuffer[index].position;
					}
					outBuffer[index].flag = inBuffer[index].flag;
					outBuffer[index].localIndex = inBuffer[index].localIndex;
					outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				}
				else
				{
					prevEdge = ( prevPosition - precedingPosition );
					currEdge = ( currPosition - prevPosition );
					nextEdge = ( nextPosition - currPosition );

					prevEdgeLength = ( prevEdge ).magnitude;
					currEdgeLength = ( currEdge ).magnitude;
					nextEdgeLength = ( nextEdge ).magnitude;

					if( prevEdgeLength * thresholdEdgeRatio >= currEdgeLength || nextEdgeLength * thresholdEdgeRatio >=  currEdgeLength )
					{
						prevEdge *= 1.0f / prevEdgeLength;
						nextEdge *= 1.0f / nextEdgeLength;

						float edgeAngleCosine = Vector3.Dot( prevEdge, nextEdge );

						if( edgeAngleCosine > thresholdAngleCosine )
						{
							outBuffer[index].flag = 0;
						}
						else
						{
							outBuffer[index].flag = inBuffer[index].flag;
						}
						outBuffer[index].localIndex = inBuffer[index].localIndex;
						outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
						outBuffer[index].position = inBuffer[index].position;
					}
				}
			}
			else
			{
				outBuffer[index].flag = inBuffer[index].flag;
				outBuffer[index].localIndex = inBuffer[index].localIndex;
				outBuffer[index].lastLocalIndex = inBuffer[index].lastLocalIndex;
				outBuffer[index].position = inBuffer[index].position;
			}
		}
	}
	#endregion
}