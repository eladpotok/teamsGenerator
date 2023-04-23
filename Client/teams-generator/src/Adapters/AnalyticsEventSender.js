import { getAnalytics, logEvent, setUserProperties } from "firebase/analytics";
import { initializeApp } from "firebase/app";
import { getDatabase } from 'firebase/database';
import { ref, child, push, update } from "firebase/database";

export class AnalyticsEventManager {

    analytics;
    db;
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
          this.db = getDatabase(app);

        //   setUserProperties(this.analytics, { : 'apples' });
        
    }

    sendContentEvent(contentType, content_id) {
        logEvent(this.analytics, 'select_content', {
            content_type: contentType,
            content_id: content_id
          });
    }

    sendPageViewEvent(page_title) {
        logEvent(this.analytics, 'page_view', {
            page_title: page_title
          });
    }

    // sendEvent2(eventType, action, data) {

    //     const postData = {
    //       author: 'username',
    //       uid: 'uid',
    //       body: 'body',
    //       title: 'title',
    //       starCount: 0,
    //       authorPic: 'picture'
    //     };
      
    //     // Get a key for a new Post.
    //     const newPostKey = push(child(ref(this.db), 'posts')).key;
      
    //     // Write the new post's data simultaneously in the posts list and the user's post list.
    //     const updates = {};
    //     updates['/posts/' + newPostKey] = postData;
    //     updates['/user-posts/' + 'uid' + '/' + newPostKey] = postData;
      
    //     return update(ref(this.db), updates);
    // }

    async sendEvent2(eventType, action, data){
        const response = await fetch(`${'https://teamsgeneratorapp-default-rtdb.firebaseio.com'}/${eventType}/${action}.json`, {
            method: 'PUT',
            body: JSON.stringify({'name': 'elad'})
        });

}



}
