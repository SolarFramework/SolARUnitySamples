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

using System.Linq;
using SolAR.Api.Display;
using SolAR.Api.Features;
using SolAR.Api.Geom;
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
    public class NaturalImage : AbstractSample
    {
        protected override void OnEnable()
        {
            base.OnEnable();

            //LOG_ADD_LOG_TO_CONSOLE();

            LOG_INFO("program is running");


            /* instantiate component manager*/
            /* this is needed in dynamic mode */
            var xpcfComponentManager = xpcf_api.getComponentManagerInstance().AddTo(subscriptions);

            if (xpcfComponentManager.load(conf.path) != XPCFErrorCode._SUCCESS)
            {
                LOG_ERROR("Failed to load the configuration file {0}", conf.path);
                enabled = false;
                return;
            }

            // declare and create components
            LOG_INFO("Start creating components");

            camera = xpcfComponentManager.create("SolARCameraOpencv").bindTo<ICamera>().AddTo(subscriptions);
            imageViewerKeypoints = xpcfComponentManager.create("SolARImageViewerOpencv", "keypoints").bindTo<IImageViewer>().AddTo(subscriptions);
            imageViewerResult = xpcfComponentManager.create("SolARImageViewerOpencv").bindTo<IImageViewer>().AddTo(subscriptions);
            marker = xpcfComponentManager.create("SolARMarker2DNaturalImageOpencv").bindTo<IMarker2DNaturalImage>().AddTo(subscriptions);
            kpDetector = xpcfComponentManager.create("SolARKeypointDetectorOpencv").bindTo<IKeypointDetector>().AddTo(subscriptions);
            matcher = xpcfComponentManager.create("SolARDescriptorMatcherKNNOpencv").bindTo<IDescriptorMatcher>().AddTo(subscriptions);
            basicMatchesFilter = xpcfComponentManager.create("SolARBasicMatchesFilter").bindTo<IMatchesFilter>().AddTo(subscriptions);
            geomMatchesFilter = xpcfComponentManager.create("SolARGeometricMatchesFilterOpencv").bindTo<IMatchesFilter>().AddTo(subscriptions);
            homographyEstimation = xpcfComponentManager.create("SolARHomographyEstimationOpencv").bindTo<I2DTransformFinder>().AddTo(subscriptions);
            homographyValidation = xpcfComponentManager.create("SolARHomographyValidation").bindTo<IHomographyValidation>().AddTo(subscriptions);
            keypointsReindexer = xpcfComponentManager.create("SolARKeypointsReIndexer").bindTo<IKeypointsReIndexer>().AddTo(subscriptions);
            poseEstimation = xpcfComponentManager.create("SolARPoseEstimationPnpOpencv").bindTo<I3DTransformFinderFrom2D3D>().AddTo(subscriptions);
            //poseEstimation =xpcfComponentManager.create("SolARPoseEstimationPnpEPFL").bindTo<I3DTransformFinderFrom2D3D>().AddTo(subscriptions);
            overlay2DComponent = xpcfComponentManager.create("SolAR2DOverlayOpencv").bindTo<I2DOverlay>().AddTo(subscriptions);
            overlay3DComponent = xpcfComponentManager.create("SolAR3DOverlayBoxOpencv").bindTo<I3DOverlay>().AddTo(subscriptions);
            img_mapper = xpcfComponentManager.create("SolARImage2WorldMapper4Marker2D").bindTo<IImage2WorldMapper>().AddTo(subscriptions);
            transform2D = xpcfComponentManager.create("SolAR2DTransform").bindTo<I2DTransform>().AddTo(subscriptions);
            descriptorExtractor = xpcfComponentManager.create("SolARDescriptorsExtractorAKAZE2Opencv").bindTo<IDescriptorsExtractor>().AddTo(subscriptions);

            /* in dynamic mode, we need to check that components are well created*/
            /* this is needed in dynamic mode */
            if (new object[] { camera, imageViewerKeypoints, imageViewerResult, marker, kpDetector, descriptorExtractor, matcher, basicMatchesFilter, geomMatchesFilter, homographyEstimation, homographyValidation, keypointsReindexer, poseEstimation, overlay2DComponent, overlay3DComponent, img_mapper, transform2D }.Contains(null))
            {
                LOG_ERROR("One or more component creations have failed");
                enabled = false;
                return;
            }
            LOG_INFO("All components have been created");

            // Declare data structures used to exchange information between components
            refImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            camImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            //kpImageCam = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            refDescriptors = SharedPtr.Alloc<DescriptorBuffer>().AddTo(subscriptions);
            camDescriptors = SharedPtr.Alloc<DescriptorBuffer>().AddTo(subscriptions);
            matches = new DescriptorMatchVector().AddTo(subscriptions);

            Hm = new Transform2Df().AddTo(subscriptions);
            // where to store detected keypoints in ref image and camera image
            refKeypoints = new KeypointList().AddTo(subscriptions);
            camKeypoints = new KeypointList().AddTo(subscriptions);

            // load marker
            LOG_INFO("LOAD MARKER IMAGE ");
            ok = marker.loadMarker();
            ok = marker.getImage(refImage);

            // NOT WORKING ! Set the size of the box to the size of the natural image marker
            var overlay3D_sizeProp = overlay3DComponent.bindTo<IConfigurable>().getProperty("size");
            //overlay3D_sizeProp.setFloatingValue(marker.getWidth(), 0);
            //overlay3D_sizeProp.setFloatingValue(marker.getHeight(), 1);
            //overlay3D_sizeProp.setFloatingValue(marker.getHeight() / 2.0f, 2);
            overlay3D_sizeProp.setFloatingValue(1, 0);
            overlay3D_sizeProp.setFloatingValue(1, 1);
            overlay3D_sizeProp.setFloatingValue(1 / 2.0f, 2);

            // detect keypoints in reference image
            LOG_INFO("DETECT MARKER KEYPOINTS ");
            kpDetector.detect(refImage, refKeypoints);

            // extract descriptors in reference image
            LOG_INFO("EXTRACT MARKER DESCRIPTORS ");
            descriptorExtractor.extract(refImage, refKeypoints, refDescriptors);
            LOG_INFO("EXTRACT MARKER DESCRIPTORS COMPUTED");

            if (camera.start() != FrameworkReturnCode._SUCCESS) // videoFile
            {
                LOG_ERROR("Camera cannot start");
                enabled = false;
                return;
            }

            // initialize overlay 3D component with the camera intrinsec parameters (please refeer to the use of intrinsec parameters file)
            overlay3DComponent.setCameraParameters(camera.getIntrinsicsParameters(), camera.getDistorsionParameters());

            // initialize pose estimation
            poseEstimation.setCameraParameters(camera.getIntrinsicsParameters(), camera.getDistorsionParameters());

            // initialize image mapper with the reference image size and marker size
            var img_mapper_config = img_mapper.bindTo<IConfigurable>().AddTo(subscriptions);
            var refSize = refImage.getSize();
            var mkSize = marker.getSize();
            img_mapper_config.getProperty("digitalWidth").setIntegerValue((int)refSize.width);
            img_mapper_config.getProperty("digitalHeight").setIntegerValue((int)refSize.height);
            img_mapper_config.getProperty("worldWidth").setFloatingValue(mkSize.width);
            img_mapper_config.getProperty("worldHeight").setFloatingValue(mkSize.height);

            // to count the average number of processed frames per seconds
            start = clock();

            // vector of 4 corners in the marker
            refImgCorners = new Point2DfList();
            float w = refImage.getWidth(), h = refImage.getHeight();
            Point2Df corner0 = new Point2Df(0, 0);
            Point2Df corner1 = new Point2Df(w, 0);
            Point2Df corner2 = new Point2Df(w, h);
            Point2Df corner3 = new Point2Df(0, h);
            refImgCorners.Add(corner0);
            refImgCorners.Add(corner1);
            refImgCorners.Add(corner2);
            refImgCorners.Add(corner3);

            pose = new Transform3Df().AddTo(subscriptions);
        }

        Transform3Df pose;

        protected void Update()
        {
            count++;
            if (camera.getNextImage(camImage) == FrameworkReturnCode._ERROR_)
                return;
            count++;

            //var matchesImage = new Image(refImage.getWidth() + camImage.getWidth(), refImage.getHeight(), refImage.getImageLayout(), refImage.getPixelOrder(), refImage.getDataType());
            //var matchImage = matchesImage;

            // detect keypoints in camera image
            kpDetector.detect(camImage, camKeypoints);
            // Not working, C2664 : cannot convert argument 1 from std::vector<boost_shared_ptr<Keypoint>> to std::vector<boost_shared_ptr<Point2Df>> !
            /* you can either draw keypoints */
            //kpDetector.drawKeypoints(camImage, camKeypoints, kpImageCam);

            /* extract descriptors in camera image*/

            descriptorExtractor.extract(camImage, camKeypoints, camDescriptors);

            /*compute matches between reference image and camera image*/
            matcher.match(refDescriptors, camDescriptors, matches);

            /* filter matches to remove redundancy and check geometric validity */
            basicMatchesFilter.filter(matches, matches, refKeypoints, camKeypoints);
            geomMatchesFilter.filter(matches, matches, refKeypoints, camKeypoints);

            /* we declare here the Solar datastucture we will need for homography*/
            var ref2Dpoints = new Point2DfList();
            var cam2Dpoints = new Point2DfList();
            //Point2Df point;
            var ref3Dpoints = new Point3DfList();
            //var output2Dpoints = new Point2DfList();
            var markerCornersinCamImage = new Point2DfList();
            var markerCornersinWorld = new Point3DfList();

            /*we consider that, if we have less than 10 matches (arbitrarily), we can't compute homography for the current frame */

            if (matches.Count > 10)
            {
                // reindex the keypoints with established correspondence after the matching
                keypointsReindexer.reindex(refKeypoints, camKeypoints, matches, ref2Dpoints, cam2Dpoints);

                // mapping to 3D points
                img_mapper.map(ref2Dpoints, ref3Dpoints);

                var res = homographyEstimation.find(ref2Dpoints, cam2Dpoints, Hm);
                //test if a meaningful matrix has been obtained
                if (res == RetCode.TRANSFORM2D_ESTIMATION_OK)
                {
                    //poseEstimation.poseFromHomography(Hm, pose, objectCorners, sceneCorners);
                    // vector of 2D corners in camera image
                    transform2D.transform(refImgCorners, Hm, markerCornersinCamImage);
                    // draw circles on corners in camera image
                    overlay2DComponent.drawCircles(markerCornersinCamImage, camImage); //DEBUG

                    /* we verify is the estimated homography is valid*/
                    if (homographyValidation.isValid(refImgCorners, markerCornersinCamImage))
                    {
                        // from the homography we create 4 points at the corners of the reference image
                        // map corners in 3D world coordinates
                        img_mapper.map(refImgCorners, markerCornersinWorld);

                        // pose from solvePNP using 4 points.
                        /* The pose could also be estimated from all the points used to estimate the homography */
                        poseEstimation.estimate(markerCornersinCamImage, markerCornersinWorld, pose);

                        /* We draw a box on the place of the recognized natural marker*/
                        overlay3DComponent.draw(pose, camImage);
                    }
                    else /* when homography is not valid*/
                        LOG_INFO("Wrong homography for this frame");
                }
            }

            /*
            // display images in viewers
            enabled = (imageViewerResult.display(camImage) == FrameworkReturnCode._SUCCESS);
            /* */

            camImage.ToUnity(ref inputTex);
        }

        protected override void OnDisable()
        {
            end = clock();
            double duration = (double)(end - start) / CLOCKS_PER_SEC;
            printf("Elasped time is {0} seconds.\n", duration);
            printf("Number of processed frame per second : {0}\n", count / duration);
            base.OnDisable();
        }

        protected void OnGUI()
        {
            if (inputTex != null)
            {
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), inputTex);
            }
        }

        // structures
        Image refImage;
        Image camImage;
        //Image kpImageCam;
        DescriptorBuffer refDescriptors;
        DescriptorBuffer camDescriptors;
        DescriptorMatchVector matches;
        Transform2Df Hm;
        KeypointList refKeypoints;
        KeypointList camKeypoints;

        // components
        new ICamera camera;
        IImageViewer imageViewerKeypoints;
        IImageViewer imageViewerResult;
        IMarker2DNaturalImage marker;
        IKeypointDetector kpDetector;
        IDescriptorMatcher matcher;
        IMatchesFilter basicMatchesFilter;
        IMatchesFilter geomMatchesFilter;
        I2DTransformFinder homographyEstimation;
        IHomographyValidation homographyValidation;
        IKeypointsReIndexer keypointsReindexer;
        I3DTransformFinderFrom2D3D poseEstimation;
        //I3DTransformFinderFrom2D3D poseEstimation;
        I2DOverlay overlay2DComponent;
        I3DOverlay overlay3DComponent;
        IImage2WorldMapper img_mapper;
        I2DTransform transform2D;
        IDescriptorsExtractor descriptorExtractor;

        Point2DfList refImgCorners;

        // to count the average number of processed frames per seconds
        [SerializeField]
        int count = 0;
        long start;
        long end;
    }
}
