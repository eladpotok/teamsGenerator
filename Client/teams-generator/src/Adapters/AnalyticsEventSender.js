import { getAnalytics, logEvent, setUserProperties } from "firebase/analytics";
import { initializeApp } from "firebase/app";

export class AnalyticsEventManager {

    analytics;

    constructor() {
        const firebaseConfig = {
            apiKey: "AIzaSyAy7Qf630Cq4vpJkR9-3a1zPMYOJYSyoV4",
            authDomain: "teamsgeneratorapp.firebaseapp.com",
            databaseURL: "https://teamsgeneratorapp-default-rtdb.firebaseio.com",
            projectId: "teamsgeneratorapp",
            storageBucket: "teamsgeneratorapp.appspot.com",
            messagingSenderId: "810692734476",
            appId: "1:810692734476:web:9f8497fe0ab8e5b727c829",
            measurementId: "G-8TP0D0S66M"
          };
        
          // Initialize Firebase
          const app = initializeApp(firebaseConfig);
          this.analytics = getAnalytics(app);
          setUserProperties(this.analytics, { favorite_food: 'apples' });
        
    }

    sendContentEvent(contentType, page_title) {
        logEvent(this.analytics, 'select_content', {
            content_type: contentType,
            page_title: page_title
          });
    }

    sendPageViewEvent(page_title) {
        logEvent(this.analytics, 'page_view', {
            page_title: page_title
          });
    }


}
