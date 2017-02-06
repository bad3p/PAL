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
							irradiancePolygon.Specular = false;
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

		for( int polygonIndex=0; polygonIndex<_irradiancePolygons.Length; polygonIndex++ )
		{
			var irradiancePolygon = _irradiancePolygons[polygonIndex];
			if( irradiancePolygon == null ) continue;

			// calculate plane of polygon in world space

			if( irradiancePolygon.numPlanePixels == 3 )
			{
				irradiancePolygon.polygonPlaneNormal = Vector3.Cross( (irradiancePolygon.worldSpacePixelPos[1]-irradiancePolygon.worldSpacePixelPos[0]).normalized, (irradiancePolygon.worldSpacePixelPos[2]-irradiancePolygon.worldSpacePixelPos[0]).normalized ).normalized;
				irradiancePolygon.polygonPlaneNormal *= -Mathf.Sign( Vector3.Dot( ( irradiancePolygon.worldSpacePixelPos[0] - _offscreenCamera.transform.position ).normalized, irradiancePolygon.polygonPlaneNormal ) );
				irradiancePolygon.pointOnPolygonPlane = irradiancePolygon.worldSpacePixelPos[0];
				irradiancePolygon.polygonPlaneTangent.Set( irradiancePolygon.polygonPlaneNormal.y, irradiancePolygon.polygonPlaneNormal.z, -irradiancePolygon.polygonPlaneNormal.x );
				irradiancePolygon.polygonPlaneBitangent = Vector3.Cross( irradiancePolygon.polygonPlaneTangent, irradiancePolygon.polygonPlaneNormal ).normalized;
				irradiancePolygon.polygonPlaneTangent = Vector3.Cross( irradiancePolygon.polygonPlaneNormal, irradiancePolygon.polygonPlaneBitangent ).normalized;
			}
			else
			{
				// edge case : all the pixels lay on the same line
				_irradiancePolygons[polygonIndex] = null;
				continue;
			}
		}
	}
}