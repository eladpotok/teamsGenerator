import React, { useState } from "react";

export const ConfigurationContext = React.createContext({
    userConfig: {},
    setUserConfig: (config) => {}
});

export const ConfigurationContextProvider = (props) => {
    const [userConfig, setUserConfig] = useState(null)

    return <ConfigurationContext.Provider value={{
        userConfig,
        setUserConfig
        
    }}>
        {props.children}    
    </ConfigurationContext.Provider>
};

