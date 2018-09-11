
# POLYGONAL AREA LIGHTS

Supported APIs:

<table>
    <tr>
	<td></td>
        <td>DirectX 11, DirectX 12</td>
        <td>OpenGL, OpenGL ES3, OpenGL ES2</td>
    </tr>
    <tr>
	<td>Rendering path</td>
        <td>Forward, Deferred</td>
        <td>Forward, Deferred</td>
    </tr>
    <tr>
	<td>Number of light sources</td>
        <td>127</td>
        <td>66</td>
    </tr>
    <tr>
	<td>Number of vertices</td>
        <td>1023</td>
        <td>198</td>
    </tr>
</table>

The vertex buffer is shared among all the light sources, so the number of light sources also depends on number of vertices per light source.

<table>
    <tr>
	<td>Number of vertices per light source</td>
        <td>DirectX 11, DirectX 12</td>
        <td>OpenGL, OpenGL ES3, OpenGL ES2</td>
    </tr>
    <tr>
	<td>3</td>
        <td>127</td>
        <td>66</td>
    </tr>
    <tr>
	<td>4</td>
        <td>127</td>
        <td>49</td>
    </tr>
    <tr>
	<td>5</td>
        <td>127</td>
        <td>39</td>
    </tr>
    <tr>
	<td>6</td>
        <td>127</td>
        <td>33</td>
    </tr>
    <tr>
	<td>7</td>
        <td>127</td>
        <td>28</td>
    </tr>
    <tr>
	<td>8</td>
        <td>127</td>
        <td>24</td>
    </tr>
    <tr>
	<td>9</td>
        <td>113</td>
        <td>22</td>
    </tr>
    <tr>
	<td>10</td>
        <td>102</td>
        <td>19</td>
    </tr>
    <tr>
	<td>20</td>
        <td>51</td>
        <td>9</td>
    </tr>
    <tr>
	<td>30</td>
        <td>34</td>
        <td>6</td>
    </tr>
    <tr>
	<td>40</td>
        <td>25</td>
        <td>4</td>
    </tr>
    <tr>
	<td>50</td>
        <td>20</td>
        <td>3</td>
    </tr>
    <tr>
	<td>60</td>
        <td>17</td>
        <td>3</td>
    </tr>
    <tr>
	<td>70</td>
        <td>14</td>
        <td>2</td>
    </tr>
</table>

# POLYGONAL AREA LIGHTS

![Example](https://media.licdn.com/mpr/mpr/AAEAAQAAAAAAAAfcAAAAJDQ5MGQyNzE4LTk3NDYtNDRiOS1hZTMxLTNiNWY2ZmJlN2NkYw.png)

In CG, the realism of illumination from area lights is due to their naturalness - because most of the natural light sources we observe in everyday life are in fact area lights. Yet in real-time computer graphics, area lights are represented mostly in some sort of pre-calculated form because of their computational expensiveness. 
The method presented in this work allows us to easily introduce illumination from arbitrary planar polygonal area lights in real-time shading.

![Example](https://media.licdn.com/mpr/mpr/AAEAAQAAAAAAAAlxAAAAJDkzNDJiODY4LTkzYTQtNDNhNC04NTQyLWJkNjcwNTE4MzM5Ng.png)
![Example](https://media.licdn.com/mpr/mpr/AAEAAQAAAAAAAAlOAAAAJGZlN2NhMTRhLWQwZTUtNGIzZC1iMmU2LTY0ODNlNDFkYTBiOQ.png)
![Example](https://media.licdn.com/mpr/mpr/AAEAAQAAAAAAAAiqAAAAJDM5ZTQyMjVkLWM5MTEtNDI3Mi05MjcxLWIyYjQ4MzM4Mzc4OQ.png)
![Example](https://github.com/bad3p/PAL/blob/master/Docs/Images/Specular.png)
