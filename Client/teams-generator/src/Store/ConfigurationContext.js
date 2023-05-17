import React, { useEffect, useState } from "react";

export const ConfigurationContext = React.createContext({
    userConfig: {},
    setUserConfig: (config) => {},
    prepareConfig: (defaultConfig) => {}
});

export const ConfigurationContextProvider = (props) => {
    const [userConfig, setUserConfig] = useState(null)

    function saveConfig(config){
        setUserConfig(config)
        localStorage.setItem('config', JSON.stringify(config))
    }

    function prepareConfig(defaultConfig){
        const configInStorage = localStorage.getItem('config')
        if (configInStorage) {
            setUserConfig(JSON.parse(configInStorage))
        }
        else {
            saveConfig(defaultConfig)
        }

    }


    return <ConfigurationContext.Provider value={{
        userConfig,
        setUserConfig: saveConfig,
        prepareConfig
        
    }}>
        {props.children}    
    </ConfigurationContext.Provider>
};

