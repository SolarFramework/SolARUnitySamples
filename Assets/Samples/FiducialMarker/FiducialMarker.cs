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

//#define VIDEO_INPUT
#define NDEBUG

using SolAR.Api.Display;
using SolAR.Api.Features;
using SolAR.Api.Geom;
using SolAR.Api.Image;
using SolAR.Api.Input.Devices;
using SolAR.Api.Input.Files;
using SolAR.Api.Solver.Pose;
using SolAR.Core;
using SolAR.Datastructure;
using UniRx;
using UnityEngine;
using XPCF.Api;
using XPCF.Core;

namespace SolAR.Samples
{
    public class FiducialMarker : AbstractSample
    {
        protected override void OnEnable()
        {
            base.OnEnable();

            LOG_ADD_LOG_TO_CONSOLE();

            /* instantiate component manager*/
            /* this is needed in dynamic mode */
            xpcfComponentManager = xpcf_api.getComponentManagerInstance();
            Disposable.Create(xpcfComponentManager.clear).AddTo(subscriptions);
            xpcfComponentManager.AddTo(subscriptions);

            if (xpcfComponentManager.load(conf.path) != XPCFErrorCode._SUCCESS)
            {
                LOG_ERROR("Failed to load the configuration file {0}", conf.path);
                enabled = false;
                return;
            }

            // declare and create components
            LOG_INFO("Start creating components");

#if VIDEO_INPUT
            camera = xpcfComponentManager.create("SolARVideoAsCameraOpencv").AddTo(subscriptions).bindTo<ICamera>().AddTo(subscriptions);
#else
            camera = xpcfComponentManager.Create("SolARCameraOpencv").AddTo(subscriptions).BindTo<ICamera>().AddTo(subscriptions);
#endif
            binaryMarker = xpcfComponentManager.Create("SolARMarker2DSquaredBinaryOpencv").AddTo(subscriptions).BindTo<IMarker2DSquaredBinary>().AddTo(subscriptions);

#if !NDEBUG
            imageViewer = xpcfComponentManager.create("SolARImageViewerOpencv").AddTo(subscriptions).bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerGrey = xpcfComponentManager.create("SolARImageViewerOpencv", "grey").AddTo(subscriptions).bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerBinary = xpcfComponentManager.create("SolARImageViewerOpencv", "binary").AddTo(subscriptions).bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerContours = xpcfComponentManager.create("SolARImageViewerOpencv", "contours").AddTo(subscriptions).bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerFilteredContours = xpcfComponentManager.create("SolARImageViewerOpencv", "filteredContours").AddTo(subscriptions).bindTo<IImageViewer>().AddTo(subscriptions);
#endif

            imageFilterBinary = xpcfComponentManager.Create("SolARImageFilterBinaryOpencv").AddTo(subscriptions).BindTo<IImageFilter>().AddTo(subscriptions);
            imageConvertor = xpcfComponentManager.Create("SolARImageConvertorOpencv").AddTo(subscriptions).BindTo<IImageConvertor>().AddTo(subscriptions);
            contoursExtractor = xpcfComponentManager.Create("SolARContoursExtractorOpencv").AddTo(subscriptions).BindTo<IContoursExtractor>().AddTo(subscriptions);
            contoursFilter = xpcfComponentManager.Create("SolARContoursFilterBinaryMarkerOpencv").AddTo(subscriptions).BindTo<IContoursFilter>().AddTo(subscriptions);
            perspectiveController = xpcfComponentManager.Create("SolARPerspectiveControllerOpencv").AddTo(subscriptions).BindTo<IPerspectiveController>().AddTo(subscriptions);
            patternDescriptorExtractor = xpcfComponentManager.Create("SolARDescriptorsExtractorSBPatternOpencv").AddTo(subscriptions).BindTo<IDescriptorsExtractorSBPattern>().AddTo(subscriptions);

            patternMatcher = xpcfComponentManager.Create("SolARDescriptorMatcherRadiusOpencv").AddTo(subscriptions).BindTo<IDescriptorMatcher>().AddTo(subscriptions);
            patternReIndexer = xpcfComponentManager.Create("SolARSBPatternReIndexer").AddTo(subscriptions).BindTo<ISBPatternReIndexer>().AddTo(subscriptions);

            img2worldMapper = xpcfComponentManager.Create("SolARImage2WorldMapper4Marker2D").AddTo(subscriptions).BindTo<IImage2WorldMapper>().AddTo(subscriptions);
            PnP = xpcfComponentManager.Create("SolARPoseEstimationPnpOpencv").AddTo(subscriptions).BindTo<I3DTransformFinderFrom2D3D>().AddTo(subscriptions);
            overlay3D = xpcfComponentManager.Create("SolAR3DOverlayBoxOpencv").AddTo(subscriptions).BindTo<I3DOverlay>().AddTo(subscriptions);
#if !NDEBUG
            overlay2DContours = xpcfComponentManager.create("SolAR2DOverlayOpencv", "contours").AddTo(subscriptions).bindTo<I2DOverlay>().AddTo(subscriptions);
            overlay2DCircles = xpcfComponentManager.create("SolAR2DOverlayOpencv", "circles").AddTo(subscriptions).bindTo<I2DOverlay>().AddTo(subscriptions);
#endif

            inputImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            greyImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            binaryImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
#if !NDEBUG
            contoursImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            filteredContoursImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
#endif

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
            pose = new Transform3Df().AddTo(subscriptions);
            //CamCalibration K;

            // components initialisation
            binaryMarker.loadMarker().Check();
            patternDescriptorExtractor.extract(binaryMarker.getPattern(), markerPatternDescriptor).Check();
            var binaryMarkerSize = binaryMarker.getSize();

            LOG_DEBUG("Marker pattern:\n {0}", binaryMarker.getPattern().getPatternMatrix());

            // Set the size of the box to display according to the marker size in world unit
            var overlay3D_sizeProp = overlay3D.BindTo<IConfigurable>().getProperty("size");
            overlay3D_sizeProp.setFloatingValue(binaryMarkerSize.width, 0);
            overlay3D_sizeProp.setFloatingValue(binaryMarkerSize.height, 1);
            overlay3D_sizeProp.setFloatingValue(binaryMarkerSize.height / 2.0f, 2);

            var patternSize = binaryMarker.getPattern().getSize();

            patternDescriptorExtractor.BindTo<IConfigurable>().getProperty("patternSize").setIntegerValue(patternSize);
            patternReIndexer.BindTo<IConfigurable>().getProperty("sbPatternSize").setIntegerValue(patternSize);

            // NOT WORKING ! initialize image mapper with the reference image size and marker size
            var img2worldMapperConf = img2worldMapper.BindTo<IConfigurable>();
            img2worldMapperConf.getProperty("digitalWidth").setIntegerValue(patternSize);
            img2worldMapperConf.getProperty("digitalHeight").setIntegerValue(patternSize);
            img2worldMapperConf.getProperty("worldWidth").setFloatingValue(binaryMarkerSize.width);
            img2worldMapperConf.getProperty("worldHeight").setFloatingValue(binaryMarkerSize.height);

            PnP.setCameraParameters(camera.getIntrinsicsParameters(), camera.getDistorsionParameters());
            overlay3D.setCameraParameters(camera.getIntrinsicsParameters(), camera.getDistorsionParameters());

            if (camera.start() != FrameworkReturnCode._SUCCESS) // Camera
            {
                LOG_ERROR("Camera cannot start");
                enabled = false;
                return;
            }

            start = clock();
        }

        protected void Update()
        {
            if (camera.getNextImage(inputImage) == FrameworkReturnCode._ERROR_)
            {
                enabled = false;
                return;
            }
            count++;

            // Convert Image from RGB to grey
            imageConvertor.convert(inputImage, greyImage, Image.ImageLayout.LAYOUT_GREY).Check();

            // Convert Image from grey to black and white
            imageFilterBinary.filter(greyImage, binaryImage).Check();

            // Extract contours from binary image
            contoursExtractor.extract(binaryImage, contours).Check();
#if !NDEBUG
            contoursImage = binaryImage.copy();
            overlay2DContours.drawContours(contours, contoursImage).Check();
#endif
            // Filter 4 edges contours to find those candidate for marker contours
            contoursFilter.filter(contours, filtered_contours).Check();

#if !NDEBUG
            filteredContoursImage = binaryImage.copy();
            overlay2DContours.drawContours(filtered_contours, filteredContoursImage).Check();
#endif
            // Create one warpped and cropped image by contour
            perspectiveController.correct(binaryImage, filtered_contours, patches).Check();

            // test if this last image is really a squared binary marker, and if it is the case, extract its descriptor
            if (patternDescriptorExtractor.extract(patches, filtered_contours, recognizedPatternsDescriptors, recognizedContours) == FrameworkReturnCode._SUCCESS)
            {

#if !NDEBUG
                var std__cout = new System.Text.StringBuilder();
                LOG_DEBUG("Looking for the following descriptor:");
                /*
                for (var i = 0; i < markerPatternDescriptor.getNbDescriptors() * markerPatternDescriptor.getDescriptorByteSize(); i++)
                {

                    if (i % patternSize == 0)
                        std__cout.Append("[");
                    if (i % patternSize != patternSize - 1)
                        std__cout.Append(markerPatternDescriptor.data()[i]).Append(", ");
                    else
                        std__cout.Append(markerPatternDescriptor.data()[i]).Append("]").AppendLine();
                }
                std__cout.AppendLine();
                */

                /*
                std__cout.Append(recognizedPatternsDescriptors.getNbDescriptors()).Append(" recognized Pattern Descriptors ").AppendLine();
                uint desrciptorSize = recognizedPatternsDescriptors.getDescriptorByteSize();
                for (uint i = 0; i < recognizedPatternsDescriptors.getNbDescriptors() / 4; i++)
                {
                    for (int j = 0; j < patternSize; j++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            std__cout.Append("[");
                            for (int l = 0; l < patternSize; l++)
                            {
                                std__cout.Append(recognizedPatternsDescriptors.data()[desrciptorSize*((i*4)+k) + j*patternSize + l]);
                                if (l != patternSize - 1)
                                    std__cout.Append(", ");
                            }
                            std__cout.Append("]");
                        }
                        std__cout.AppendLine();
                    }
                    std__cout.AppendLine().AppendLine();
                }
                */

                /*
                std__cout.Append(recognizedContours.Count).Append(" Recognized Pattern contour ").AppendLine();
                for (int i = 0; i < recognizedContours.Count / 4; i++)
                {
                    for (int j = 0; j < recognizedContours[i].Count; j++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            std__cout.Append("[").Append(recognizedContours[i * 4 + k][j].getX()).Append(", ").Append(recognizedContours[i * 4 + k][j].getY()).Append("] ");
                        }
                        std__cout.AppendLine();
                    }
                    std__cout.AppendLine().AppendLine();
                }
                std__cout.AppendLine();
                */
#endif

                // From extracted squared binary pattern, match the one corresponding to the squared binary marker
                if (patternMatcher.match(markerPatternDescriptor, recognizedPatternsDescriptors, patternMatches) == Api.Features.RetCode.DESCRIPTORS_MATCHER_OK)
                {
#if !NDEBUG
                    std__cout.Append("Matches :").AppendLine();
                    for (int num_match = 0; num_match < patternMatches.Count; num_match++)
                        std__cout.Append("Match [").Append(patternMatches[num_match].getIndexInDescriptorA()).Append(",").Append(patternMatches[num_match].getIndexInDescriptorB()).Append("], dist = ").Append(patternMatches[num_match].getMatchingScore()).AppendLine();
                    std__cout.AppendLine().AppendLine();
#endif

                    // Reindex the pattern to create two vector of points, the first one corresponding to marker corner, the second one corresponding to the poitsn of the contour
                    patternReIndexer.reindex(recognizedContours, patternMatches, pattern2DPoints, img2DPoints).Check();
#if !NDEBUG
                    LOG_DEBUG("2D Matched points :");
                    for (int i = 0; i < img2DPoints.Count; i++)
                        LOG_DEBUG("{0}", img2DPoints[i]);
                    for (int i = 0; i < pattern2DPoints.Count; i++)
                        LOG_DEBUG("{0}", pattern2DPoints[i]);
                    overlay2DCircles.drawCircles(img2DPoints, inputImage);
#endif
                    // Compute the 3D position of each corner of the marker
                    img2worldMapper.map(pattern2DPoints, pattern3DPoints).Check();
#if !NDEBUG
                    std__cout.Append("3D Points position:").AppendLine();
                    for (int i = 0; i < pattern3DPoints.Count; i++)
                        LOG_DEBUG("{0}", pattern3DPoints[i]);
#endif
                    // Compute the pose of the camera using a Perspective n Points algorithm using only the 4 corners of the marker
                    if (PnP.estimate(img2DPoints, pattern3DPoints, pose) == FrameworkReturnCode._SUCCESS)
                    {
                        //LOG_DEBUG("Camera pose : \n {0}", pose.ToUnity());
                        // Display a 3D box over the marker
                        overlay3D.draw(pose, inputImage);
                    }
                }
#if !NDEBUG
                Debug.Log(std__cout.ToString());
                std__cout.Clear();
#endif
            }

            // display images in viewers
#if !NDEBUG
            enabled = (imageViewer.display(inputImage) == FrameworkReturnCode._SUCCESS);
            enabled = (imageViewerGrey.display(greyImage) == FrameworkReturnCode._SUCCESS);
            enabled = (imageViewerBinary.display(binaryImage) == FrameworkReturnCode._SUCCESS);
            enabled = (imageViewerContours.display(contoursImage) == FrameworkReturnCode._SUCCESS);
            enabled = (imageViewerFilteredContours.display(filteredContoursImage) == FrameworkReturnCode._SUCCESS);
#endif

            inputImage.ToUnity(ref inputTex);
        }

        protected override void OnDisable()
        {
            end = clock();
            double duration = (double)(end - start) / CLOCKS_PER_SEC;
            printf("Elasped time is {0} seconds.", duration);
            printf("Number of processed frames per second : {0}", count / duration);
            base.OnDisable();
        }

        // structures
        Image inputImage;
        Image greyImage;
        Image binaryImage;
#if !NDEBUG
        Image contoursImage;
        Image filteredContoursImage;
#endif

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

#if !NDEBUG
        IImageViewer imageViewer;
        IImageViewer imageViewerGrey;
        IImageViewer imageViewerBinary;
        IImageViewer imageViewerContours;
        IImageViewer imageViewerFilteredContours;
#endif

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
#if !NDEBUG
        I2DOverlay overlay2DContours;
        I2DOverlay overlay2DCircles;
#endif

        // to count the average number of processed frames per seconds
        int count = 0;
        long start;
        long end;

        protected void OnGUI()
        {
            if (inputTex != null)
            {
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), inputTex);
            }
        }
    }
}
