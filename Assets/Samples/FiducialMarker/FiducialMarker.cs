/**
* @copyright Copyright (c) 2017 B-com http://www.b-com.com/
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*     http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using SolAR.Api.Display;
using SolAR.Api.Features;
using SolAR.Api.Geom;
using SolAR.Api.Image;
using SolAR.Api.Input.Devices;
using SolAR.Api.Input.Files;
using SolAR.Api.Solver.Pose;
using SolAR.Core;
using SolAR.Datastructure;
using UnityEngine;
using UnityEngine.Assertions;
using XPCF;

namespace SolAR.Samples
{
    public class FiducialMarker : AbstractSample
    {
        // structures
        Image inputImage;
        Image greyImage;
        Image binaryImage;

        Contour2DfList contours;
        Contour2DfList filtered_contours;
        ImageList patches;
        Contour2DfList recognizedContours;
        DescriptorBuffer recognizedPatternsDescriptors;
        DescriptorBuffer markerPatternDescriptor;
        DescriptorMatchVector patternMatches;
        Point2DfList pattern2DPoints;
        Point2DfList img2DPoints;
        Point3DfList pattern3DPoints;
        Transform3Df pose;

        IComponentManager xpcfComponentManager;

        // components
        new ICamera camera;
        IMarker2DSquaredBinary binaryMarker;

        /*
        IImageViewer imageViewer;
        IImageViewer imageViewerGrey;
        IImageViewer imageViewerBinary;
        */

        IImageConvertor imageConvertor;
        IImageFilter imageFilterBinary;
        IContoursExtractor contoursExtractor;
        IContoursFilter contoursFilter;
        IPerspectiveController perspectiveController;
        IDescriptorsExtractorSBPattern patternDescriptorExtractor;

        IDescriptorMatcher patternMatcher;
        ISBPatternReIndexer patternReIndexer;

        IImage2WorldMapper img2worldMapper;
        I3DTransformFinderFrom2D3D PnP;
        I3DOverlay overlay3D;

        // to count the average number of processed frames per seconds
        [UnityEngine.SerializeField]
        int count = 0;
        long start;
        long end;

        protected void Awake()
        {
            // structures
            inputImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            greyImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            binaryImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);

            contours = new Contour2DfList().AddTo(subscriptions);
            filtered_contours = new Contour2DfList().AddTo(subscriptions);
            patches = new ImageList().AddTo(subscriptions);
            recognizedContours = new Contour2DfList().AddTo(subscriptions);
            recognizedPatternsDescriptors = new DescriptorBuffer().AddTo(subscriptions);
            markerPatternDescriptor = new DescriptorBuffer().AddTo(subscriptions);
            patternMatches = new DescriptorMatchVector().AddTo(subscriptions);
            pattern2DPoints = new Point2DfList().AddTo(subscriptions);
            img2DPoints = new Point2DfList().AddTo(subscriptions);
            pattern3DPoints = new Point3DfList().AddTo(subscriptions);
            pose = SharedPtr.Alloc<Transform3Df>();
        }

        protected void Start()
        {
            /* instantiate component manager*/
            /* this is needed in dynamic mode */
            xpcfComponentManager = xpcf.getComponentManagerInstance().AddTo(subscriptions);

            if (xpcfComponentManager.load(conf.path) != XPCFErrorCode._SUCCESS)
            {
                LOG_ERROR("Failed to load the configuration file conf_FiducialMarker.xml");
                enabled = false;
                return;
            }

            // declare and create components
            LOG_INFO("Start creating components");

            camera = xpcfComponentManager.create("SolARCameraOpencv").bindTo<ICamera>().AddTo(subscriptions);
            binaryMarker = xpcfComponentManager.create("SolARMarker2DSquaredBinaryOpencv").bindTo<IMarker2DSquaredBinary>().AddTo(subscriptions);

            /*
            imageViewer = xpcfComponentManager.create("SolARImageViewerOpencv").bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerGrey = xpcfComponentManager.create("SolARImageViewerOpencv", "grey").bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerBinary = xpcfComponentManager.create("SolARImageViewerOpencv", "binary").bindTo<IImageViewer>().AddTo(subscriptions);
            */

            imageConvertor = xpcfComponentManager.create("SolARImageConvertorOpencv").bindTo<IImageConvertor>().AddTo(subscriptions);
            imageFilterBinary = xpcfComponentManager.create("SolARImageFilterBinaryOpencv").bindTo<IImageFilter>().AddTo(subscriptions);
            contoursExtractor = xpcfComponentManager.create("SolARContoursExtractorOpencv").bindTo<IContoursExtractor>().AddTo(subscriptions);
            contoursFilter = xpcfComponentManager.create("SolARContoursFilterBinaryMarkerOpencv").bindTo<IContoursFilter>().AddTo(subscriptions);
            perspectiveController = xpcfComponentManager.create("SolARPerspectiveControllerOpencv").bindTo<IPerspectiveController>().AddTo(subscriptions);
            patternDescriptorExtractor = xpcfComponentManager.create("SolARDescriptorsExtractorSBPatternOpencv").bindTo<IDescriptorsExtractorSBPattern>().AddTo(subscriptions);

            patternMatcher = xpcfComponentManager.create("SolARDescriptorMatcherRadiusOpencv").bindTo<IDescriptorMatcher>().AddTo(subscriptions);
            patternReIndexer = xpcfComponentManager.create("SolARSBPatternReIndexer").bindTo<ISBPatternReIndexer>().AddTo(subscriptions);

            img2worldMapper = xpcfComponentManager.create("SolARImage2WorldMapper4Marker2D").bindTo<IImage2WorldMapper>().AddTo(subscriptions);
            PnP = xpcfComponentManager.create("SolARPoseEstimationPnpOpencv").bindTo<I3DTransformFinderFrom2D3D>().AddTo(subscriptions);
            overlay3D = xpcfComponentManager.create("SolAR3DOverlayBoxOpencv").bindTo<I3DOverlay>().AddTo(subscriptions);

            // components initialisation
            ok = binaryMarker.loadMarker();
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);
            ok = patternDescriptorExtractor.extract(binaryMarker.getPattern(), markerPatternDescriptor);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // LOG_DEBUG("Marker pattern:\n {}", binaryMarker.getPattern().getPatternMatrix());

            // Set the size of the box to display according to the marker size in world unit
            var overlay3D_sizeProp = overlay3D.bindTo<IConfigurable>().getProperty("size");
            var size = binaryMarker.getSize();
            overlay3D_sizeProp.setFloatingValue(size.width, 0);
            overlay3D_sizeProp.setFloatingValue(size.height, 1);
            overlay3D_sizeProp.setFloatingValue(size.height / 2.0f, 2);

            int patternSize = binaryMarker.getPattern().getSize();

            patternDescriptorExtractor.bindTo<IConfigurable>().getProperty("patternSize").setIntegerValue(patternSize);
            patternReIndexer.bindTo<IConfigurable>().getProperty("sbPatternSize").setIntegerValue(patternSize);

            // NOT WORKING ! initialize image mapper with the reference image size and marker size
            var img2worldMapperConf = img2worldMapper.bindTo<IConfigurable>();
            img2worldMapperConf.getProperty("digitalWidth").setIntegerValue(patternSize);
            img2worldMapperConf.getProperty("digitalHeight").setIntegerValue(patternSize);
            img2worldMapperConf.getProperty("worldWidth").setFloatingValue(binaryMarker.getSize().width);
            img2worldMapperConf.getProperty("worldHeight").setFloatingValue(binaryMarker.getSize().height);

            PnP.setCameraParameters(camera.getIntrinsicsParameters(), camera.getDistorsionParameters()); //TODO
            overlay3D.setCameraParameters(camera.getIntrinsicsParameters(), camera.getDistorsionParameters()); //TODO

            if (camera.start() != FrameworkReturnCode._SUCCESS) // Camera
            {
                LOG_ERROR("Camera cannot start");
                enabled = false;
            }

            start = clock();
        }

        protected void Update()
        {
            if (camera.getNextImage(inputImage) == FrameworkReturnCode._ERROR_)
                return;
            count++;

            // Convert Image from RGB to grey
            ok = imageConvertor.convert(inputImage, greyImage, Image.ImageLayout.LAYOUT_GREY);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Convert Image from grey to black and white
            ok = imageFilterBinary.filter(greyImage, binaryImage);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Extract contours from binary image
            ok = contoursExtractor.extract(binaryImage, contours);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Filter 4 edges contours to find those candidate for marker contours
            ok = contoursFilter.filter(contours, filtered_contours);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Create one warpped and cropped image by contour
            ok = perspectiveController.correct(binaryImage, filtered_contours, patches);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // test if this last image is really a squared binary marker, and if it is the case, extract its descriptor
            if (patternDescriptorExtractor.extract(patches, filtered_contours, recognizedPatternsDescriptors, recognizedContours) == FrameworkReturnCode._SUCCESS)
            {
                // From extracted squared binary pattern, match the one corresponding to the squared binary marker
                if (patternMatcher.match(markerPatternDescriptor, recognizedPatternsDescriptors, patternMatches) == DescriptorMatcherRetCode.DESCRIPTORS_MATCHER_OK)
                {
                    // Reindex the pattern to create two vector of points, the first one corresponding to marker corner, the second one corresponding to the poitsn of the contour
                    patternReIndexer.reindex(recognizedContours, patternMatches, pattern2DPoints, img2DPoints);

                    // Compute the 3D position of each corner of the marker
                    img2worldMapper.map(pattern2DPoints, pattern3DPoints);

                    // Compute the pose of the camera using a Perspective n Points algorithm using only the 4 corners of the marker
                    if (PnP.estimate(img2DPoints, pattern3DPoints, pose) == FrameworkReturnCode._SUCCESS)
                    {
                        // LOG_DEBUG("Camera pose : \n {}", pose.matrix());
                        // Display a 3D box over the marker
                        overlay3D.draw(pose, inputImage);
                    }
                }
            }

            /*
            // display images in viewers
            enabled = (imageViewer.display(inputImage) == FrameworkReturnCode._SUCCESS);
            enabled = (imageViewerGrey.display(greyImage) == FrameworkReturnCode._SUCCESS);
            enabled = (imageViewerBinary.display(binaryImage) == FrameworkReturnCode._SUCCESS);
            */

            {
                var w = (int)inputImage.getWidth();
                var h = (int)inputImage.getHeight();
                Assert.AreEqual(3, inputImage.getNbChannels());
                Assert.AreEqual(8, inputImage.getNbBitsPerComponent());
                Assert.AreEqual(Image.DataType.TYPE_8U, inputImage.getDataType());
                Assert.AreEqual(Image.ImageLayout.LAYOUT_BGR, inputImage.getImageLayout());
                Assert.AreEqual(Image.PixelOrder.INTERLEAVED, inputImage.getPixelOrder());
                if (inputTex != null && (inputTex.width != w || inputTex.height != h))
                {
                    Destroy(inputTex);
                    inputTex = null;
                }
                if (inputTex == null)
                {
                    inputTex = new Texture2D(w, h, TextureFormat.RGB24, false);
                }
                inputTex.LoadRawTextureData(inputImage.data(), (int)inputImage.getBufferSize());
                inputTex.Apply();
            }
        }

        protected override void OnDisable()
        {
            end = clock();
            double duration = (double)(end - start) / CLOCKS_PER_SEC;
            printf("Elasped time is {0} seconds.", duration);
            printf("Number of processed frames per second : {0}", count / duration);
            base.OnDisable();
        }

        Texture2D inputTex;

        protected void OnGUI()
        {
            if (inputTex != null)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), inputTex);
        }
    }
}
