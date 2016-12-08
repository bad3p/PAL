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
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;

[CanEditMultipleObjects]
[CustomEditor(typeof(MeshAreaLight))]
public class MeshAreaLightEditor : Editor
{
	void Awake()
	{ 
	}

	void OnDestroy()
	{
	}

	public override void OnInspectorGUI()
	{
		GUILayout.Label( "Batch stats:" );
		GUILayout.Label( "Num polygons: " + PALBatchBuilder.NumPolygons );
		GUILayout.Label( "Num vertices: " + PALBatchBuilder.NumVertices );
		GUILayout.Label( "Buffer size: " + PALBatchBuilder.BufferSize + "/" + PALBatchBuilder.ShaderConstantBufferSize );

		DrawDefaultInspector();

		MeshAreaLight thisMeshAreaLight = this.target as MeshAreaLight;
		foreach( var otherMeshAreaLight in GameObject.FindObjectsOfType<MeshAreaLight>() )
		{
			if( thisMeshAreaLight != otherMeshAreaLight )
			{
				otherMeshAreaLight.ProjectionMode = thisMeshAreaLight.ProjectionMode;
			}
		}
	}
}
